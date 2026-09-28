using Grimoire.Hub;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a browser with a page open is sent: a snapshot to open with, and then one payload per
/// change that has something to tell that reader (ACCESS-005, ACCESS-006).
/// </summary>
/// <remarks>
/// <para>
/// The payloads here are plain strings, not the shapes any one view sends. What is under test is the
/// mechanism — when a subscriber is woken, what it is given first, and what it is <em>not</em> given
/// — and a test written against a view's record would fail when that view gains a field.
/// </para>
/// <para>
/// Nothing waits for real time: every change is published on the test's own thread and read back
/// through the enumerator, so the Fast suite's 15 s budget is never touched.
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class LiveUpdatesTests
{
    private const string Topic = "a-view";

    private readonly LiveUpdates live = new();

    [Fact]
    public async Task Watch_OpensWithASnapshot()
    {
        using var watching = new CancellationTokenSource();
        await using var events = Reading(_ => ["the whole of it"], _ => ["something more"], watching.Token);

        // The first event of every stream is the snapshot, before anything has changed. It is what
        // answers a reconnect with no replay buffer behind it (research.md R-01).
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("the whole of it", events.Current);
    }

    [Fact]
    public async Task Watch_SendsAnIncrement_AfterAChange()
    {
        using var watching = new CancellationTokenSource();
        await using var events = Reading(_ => ["the whole of it"], _ => ["something more"], watching.Token);

        Assert.True(await events.MoveNextAsync());

        live.Changed(Topic);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("something more", events.Current);
    }

    [Fact]
    public async Task Watch_OpensWithASnapshotOfItsOwn_WhenASecondReaderJoins()
    {
        using var watching = new CancellationTokenSource();
        await using var first = Reading(_ => ["the whole of it"], _ => ["something more"], watching.Token);

        Assert.True(await first.MoveNextAsync());
        Assert.Equal("the whole of it", first.Current);

        // A second tab on the same view. It is not caught up from the first reader's stream — it opens
        // with a snapshot of its own, which is the only thing that can tell it where the view stands.
        await using var second = Reading(_ => ["the whole of it"], _ => ["something more"], watching.Token);

        Assert.True(await second.MoveNextAsync());
        Assert.Equal("the whole of it", second.Current);
    }

    [Fact]
    public async Task Watch_WakesEveryReaderOfTheView_AfterAChange()
    {
        using var watching = new CancellationTokenSource();
        await using var first = Reading(_ => ["the whole of it"], _ => ["from the first"], watching.Token);
        await using var second = Reading(_ => ["the whole of it"], _ => ["from the second"], watching.Token);

        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());

        // Nothing addresses one subscriber: a change wakes all of them, and each then works out for
        // itself what it has not been sent.
        live.Changed(Topic);

        Assert.True(await first.MoveNextAsync());
        Assert.Equal("from the first", first.Current);
        Assert.True(await second.MoveNextAsync());
        Assert.Equal("from the second", second.Current);
    }

    [Fact]
    public async Task Watch_SendsNothingOfWhatChangedWhileNobodyWasReading()
    {
        var increments = 0;

        // Published with the view closed. There is no subscriber and therefore nobody to write to.
        live.Changed(Topic);
        live.Changed(Topic);

        using var watching = new CancellationTokenSource();
        await using var events = Reading(
            _ => ["the whole of it"],
            _ =>
            {
                increments++;
                return [$"increment {increments}"];
            },
            watching.Token);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("the whole of it", events.Current);

        // Asserted through the delegate rather than by awaiting an event that must never arrive: the
        // iterator is suspended on the snapshot it just yielded, so a replayed change would already
        // have been worked out here. Nothing was buffered, nothing expires, and the snapshot is what
        // carried whatever happened while the page was away (ACCESS-005, research.md R-01).
        Assert.Equal(0, increments);

        live.Changed(Topic);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("increment 1", events.Current);
    }

    [Fact]
    public async Task Watch_SendsSeveralPayloads_WhenOneChangeIsTwoThingsToSay()
    {
        using var watching = new CancellationTokenSource();
        await using var events = Reading(
            _ => ["the whole of it"],
            _ => ["something missing", "something appended"],
            watching.Token);

        Assert.True(await events.MoveNextAsync());

        // A record that grew while entries of it were also lost is two payloads. Were one change worth
        // only one, the second would wait for a change that may never come.
        live.Changed(Topic);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("something missing", events.Current);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("something appended", events.Current);
    }

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task Watch_SendsOnlyWhatIsPastWhatThisReaderHasBeenSent()
    {
        // A record, as the growing thing the one incremental stream watches. How far this reader has
        // been sent is the whole of what is kept for it (ACCESS-006, research.md R-05).
        var record = "the frame\n";

        IEnumerable<string> Unsent(Sent sent)
        {
            if (record.Length <= sent.Bytes)
            {
                yield break;
            }

            var append = record[sent.Bytes..];
            sent.Bytes = record.Length;
            yield return append;
        }

        using var watching = new CancellationTokenSource();
        await using var events = Reading(Unsent, Unsent, watching.Token);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("the frame\n", events.Current);

        record += "read ada.md\n";
        live.Changed(Topic);

        // Only what was appended, never the record again: it reaches the low hundreds of kilobytes,
        // and sending it whole per growth would send the whole of it once per tool result.
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("read ada.md\n", events.Current);

        // A change with nothing past this reader's offset says nothing at all. A subscriber woken for
        // a change its own snapshot already carried is what makes registering before the snapshot
        // cost nothing.
        live.Changed(Topic);
        record += "wrote ada.md\n";
        live.Changed(Topic);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("wrote ada.md\n", events.Current);
    }

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task Watch_SendsEachReaderOnlyWhatIsPastItsOwnOffset_WhenTheyJoinedAtDifferentTimes()
    {
        var record = "the frame\n";
        var openedAt = new List<int>();

        IEnumerable<string> Unsent(Sent sent)
        {
            if (record.Length <= sent.Bytes)
            {
                yield break;
            }

            var append = record[sent.Bytes..];
            sent.Bytes = record.Length;
            yield return append;
        }

        IEnumerable<string> Opening(Sent sent)
        {
            openedAt.Add(sent.Bytes);
            return Unsent(sent);
        }

        using var watching = new CancellationTokenSource();
        await using var early = Reading(Opening, Unsent, watching.Token);

        Assert.True(await early.MoveNextAsync());
        Assert.Equal("the frame\n", early.Current);

        // The record grows while only the first reader is there, and nothing wakes it: it is behind.
        record += "read ada.md\n";

        await using var late = Reading(Opening, Unsent, watching.Token);

        // The second reader opens with the whole of what is there *then* — the part the first reader
        // has still not been sent included.
        Assert.True(await late.MoveNextAsync());
        Assert.Equal("the frame\nread ada.md\n", late.Current);

        // Nothing is kept per subscriber but its own offset, and a new subscriber's starts at nothing.
        Assert.Equal([0, 0], openedAt);

        record += "wrote ada.md\n";
        live.Changed(Topic);

        // Each is sent what is past its own offset and no more: the reader that was behind gets the
        // middle of the record as well, the one whose snapshot already carried it does not.
        Assert.True(await early.MoveNextAsync());
        Assert.Equal("read ada.md\nwrote ada.md\n", early.Current);

        Assert.True(await late.MoveNextAsync());
        Assert.Equal("wrote ada.md\n", late.Current);
    }

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task Watch_TellsThisReaderOfALossOnce()
    {
        var lost = 0;

        IEnumerable<string> Gaps(Sent sent)
        {
            if (lost <= sent.EntriesLost)
            {
                yield break;
            }

            sent.EntriesLost = lost;
            yield return $"{lost} missing";
        }

        using var watching = new CancellationTokenSource();
        await using var events = Reading(Gaps, Gaps, watching.Token);

        // Nothing lost yet, so the snapshot says nothing about a gap and the first real event is the
        // first loss. How many losses this reader has been told about is the second and last thing
        // kept for it (ACCESS-006, RUNS-007).
        lost = 1;
        live.Changed(Topic);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("1 missing", events.Current);

        // Woken again with the count unmoved, it is told nothing; the next thing it hears is the
        // count having risen, not the same gap twice.
        live.Changed(Topic);
        lost = 3;
        live.Changed(Topic);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal("3 missing", events.Current);
    }

    /// <summary>
    /// One reader of <see cref="Topic"/>, as the host's stream would drain it.
    /// </summary>
    /// <remarks>
    /// The enumerator, not the sequence: the subscription is registered when enumeration starts, so a
    /// test that never moved it would publish to nobody. Every caller disposes it, which is what takes
    /// the subscriber off the list.
    /// </remarks>
    private IAsyncEnumerator<string> Reading(
        Func<Sent, IEnumerable<string>> opening,
        Func<Sent, IEnumerable<string>> next,
        CancellationToken token) =>
        live.Watch(Topic, opening, next, token).GetAsyncEnumerator(token);
}

using System.Text;
using Grimoire.Agent;
using Grimoire.Runs;
using Grimoire.Runs.Adapters;

namespace Grimoire.Contract.Tests;

/// <summary>
/// The record against a real filesystem: where the file is, that it is text, that what was appended
/// before a stop is there afterwards, and that a directory it cannot write to is counted rather than
/// thrown (RUNS-007).
/// </summary>
/// <remarks>
/// All four are the filesystem's decisions, and an in-memory adapter cannot make any of them true.
/// What the file's Markdown looks like is <c>RecordText</c>'s and is proven a level down, without a
/// disk (<c>RecordTextTests</c>).
/// </remarks>
[Trait("level", "contract")]
[Trait("req", "RUNS-007")]
public sealed class MarkdownRunRecordTests : IDisposable
{
    private readonly string state = Directory.CreateTempSubdirectory("grimoire-record-").FullName;

    private static readonly DateTimeOffset Noon = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(state, recursive: true);

    [Fact]
    public void Record_IsAMarkdownFileNamedAfterTheRun()
    {
        var head = AHead();

        new MarkdownRunRecord(state).Begin(head);

        // `<state>/runs/<runId>.md`, beside `submissions.db` and never in the wiki
        // (contracts/run-record.md).
        var file = Path.Combine(state, "runs", $"{head.RunId}.md");
        Assert.True(File.Exists(file));
        Assert.Contains($"# Run {head.RunId}", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_HoldsWhatWasAppended_WhenTheRunIsCutOffPartWayThroughItsNarrative()
    {
        var head = AHead();

        // One record written by a Grimoire that is then gone: nothing closes the file, nothing
        // flushes, and no tail is ever written. The appends are what has to have survived (RUNS-004).
        var record = new MarkdownRunRecord(state);
        record.Begin(head);
        record.Append(Called(head.RunId, "read_page", """{"path":"ada.md"}"""));
        record.Append(Returned(head.RunId, "# Ada\n"));

        // A second adapter over the same directory, as a second process's would be.
        var afterwards = File.ReadAllText(Path.Combine(state, "runs", $"{head.RunId}.md"));

        Assert.Contains("called read_page", afterwards, StringComparison.Ordinal);
        Assert.Contains("# Ada", afterwards, StringComparison.Ordinal);
        Assert.DoesNotContain("ended", afterwards, StringComparison.Ordinal);
        Assert.Equal(0, new MarkdownRunRecord(state).EntriesLost(head.RunId));
    }

    [Fact]
    public void Record_KeepsEveryAppendInOrder()
    {
        var head = AHead();
        var record = new MarkdownRunRecord(state);

        record.Begin(head);
        record.Append(Called(head.RunId, "read_page", "{}"));
        record.Append(Returned(head.RunId, "first"));
        record.Append(Called(head.RunId, "write_page", "{}"));
        record.Ended(ATail(head.RunId, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry));

        var text = File.ReadAllText(Path.Combine(state, "runs", $"{head.RunId}.md"));

        // In file order, which is the order they happened. Nothing already on disk was moved.
        Assert.True(
            text.IndexOf("called read_page", StringComparison.Ordinal)
            < text.IndexOf("read_page returned", StringComparison.Ordinal));
        Assert.True(
            text.IndexOf("read_page returned", StringComparison.Ordinal)
            < text.IndexOf("called write_page", StringComparison.Ordinal));
        Assert.True(
            text.IndexOf("called write_page", StringComparison.Ordinal)
            < text.IndexOf("ended done", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_IsCountedAndDoesNotThrow_WhenTheRecordCannotBeWritten()
    {
        var head = AHead();

        // A file where the directory has to be, put there before the adapter exists: the real
        // filesystem then refuses to make the directory and every write under it, which is the closest
        // a test can come to a full disk without one.
        File.WriteAllText(Path.Combine(state, "runs"), "not a directory");

        var record = new MarkdownRunRecord(state);

        record.Begin(head);
        record.Append(Called(head.RunId, "read_page", "{}"));

        // Nothing thrown, and the run would have gone on (RUNS-007, research.md R-10).
        Assert.Equal(2, record.EntriesLost(head.RunId));
    }

    [Fact]
    public void LostEntries_AreSaidInTheRecord_OnceAWriteSucceedsAgain()
    {
        var head = AHead();
        var runs = Path.Combine(state, "runs");

        File.WriteAllText(runs, "not a directory");

        var record = new MarkdownRunRecord(state);
        record.Begin(head);
        record.Append(Called(head.RunId, "read_page", "{}"));

        // The disk comes back.
        File.Delete(runs);
        Directory.CreateDirectory(runs);

        record.Append(Returned(head.RunId, "# Ada\n"));

        var text = File.ReadAllText(Path.Combine(runs, $"{head.RunId}.md"));

        // The gap is where it happened: in front of the entry that finally got through, so a reader
        // sees it between the moment before it and the moment after.
        Assert.Contains("2 entries", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("2 entries", StringComparison.Ordinal)
            < text.IndexOf("read_page returned", StringComparison.Ordinal));
        Assert.Equal(2, record.EntriesLost(head.RunId));
    }

    [Fact]
    public void Tail_CanStillBeWritten_WhenItsFirstWriteFailed()
    {
        var head = AHead();
        var runs = Path.Combine(state, "runs");
        var record = new MarkdownRunRecord(state);

        record.Begin(head);

        // The disk goes away just as the run ends.
        Directory.Delete(runs, recursive: true);
        File.WriteAllText(runs, "not a directory");

        record.Ended(ATail(head.RunId, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry));

        Assert.Equal(1, record.EntriesLost(head.RunId));

        // And comes back. A tail that could not be written is not a tail: had the run been marked
        // ended on the attempt rather than on the write, the record could never have got one at all,
        // and it would be missing its tail with nothing able to put one there (RUNS-007).
        File.Delete(runs);
        Directory.CreateDirectory(runs);

        record.Ended(ATail(head.RunId, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry));

        var text = File.ReadAllText(Path.Combine(runs, $"{head.RunId}.md"));
        Assert.Contains("ended done", text, StringComparison.Ordinal);
        Assert.Contains("1 entries", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_TakesNoMoment_AfterItsTail()
    {
        var head = AHead();
        var record = new MarkdownRunRecord(state);

        record.Begin(head);
        record.Ended(ATail(head.RunId, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry));

        var file = Path.Combine(state, "runs", $"{head.RunId}.md");
        var ended = File.ReadAllText(file);

        record.Append(Returned(head.RunId, "one word too late"));

        // Nothing is written after the tail, and nothing is counted lost: no write failed
        // (contracts/run-record.md).
        Assert.Equal(ended, File.ReadAllText(file));
        Assert.Equal(0, record.EntriesLost(head.RunId));
    }

    [Fact]
    [Trait("req", "ACCESS-006")]
    public async Task Read_IsAlwaysAWholeRecord_WhileTheRunIsStillAppendingToIt()
    {
        var head = AHead();
        var record = new MarkdownRunRecord(state);

        record.Begin(head);

        var headText = RecordText.Head(head);

        // About a megabyte, which is large enough that one append is several writes to the disk rather
        // than one — measured: at a few kilobytes the window is so small that a read misses it two
        // times in three, and a test that passes on broken code proves nothing. The character outside
        // ASCII is there so that a read landing inside a write is not even valid UTF-8.
        var moment = Returned(head.RunId, string.Concat(Enumerable.Repeat("Ada Lovelace — 42\n", 60_000)));
        var momentText = RecordText.Moment(moment);

        const int appends = 12;

        var appending = Task.Run(
            () =>
            {
                for (var i = 0; i < appends; i++)
                {
                    record.Append(moment);
                }
            },
            TestContext.Current.CancellationToken);

        var readings = 0;

        while (!appending.IsCompleted)
        {
            if (record.Read(head.RunId) is not { } bytes)
            {
                continue;
            }

            readings++;

            // Every answer is a record taken at an append boundary: the head, and some whole number of
            // moments behind it. `File.AppendAllText` is not atomic, so a read that did not wait for
            // one would serve a segment cut in half — a record the browser cannot segment, on the very
            // poll where the run is most alive (ACCESS-006).
            var text = Encoding.UTF8.GetString(bytes);
            var behindTheHead = text.Length - headText.Length;

            Assert.StartsWith(headText, text, StringComparison.Ordinal);
            Assert.Equal(0, behindTheHead % momentText.Length);
            Assert.Equal(headText + string.Concat(Enumerable.Repeat(momentText, behindTheHead / momentText.Length)), text);
        }

        await appending;

        // The loop has to have actually looked. A run that finished appending before the first read
        // would pass this without having read anything at all.
        Assert.True(readings > 0, "the record was never read while it was being appended to");
    }

    private static RunFrameHead AHead() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "claude-opus-4-5-20251101",
        ToolGrant.ForIngest,
        Noon,
        Ceilings.Fixed,
        Noon);

    private static RunFrameTail ATail(Guid runId, RunOutcome outcome, RunEndedBecause because) => new(
        runId,
        Noon.AddMinutes(3),
        outcome,
        because,
        TimeSpan.FromMinutes(3),
        TokensUsed: 148_233,
        Ceilings.Fixed,
        new Dictionary<string, ModelTokens>(StringComparer.Ordinal));

    private static RunMoment Called(Guid runId, string tool, string arguments) =>
        new(runId, Noon, RunMomentKind.ToolCalled, tool, arguments);

    private static RunMoment Returned(Guid runId, string content) =>
        new(runId, Noon, RunMomentKind.ToolReturned, "read_page", content);
}

using System.Text;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The line a record gets once a write succeeds again, saying how many entries were lost before it
/// (RUNS-007). The real adapter writes a sentence; what both have to do is put the gap where it
/// happened, so a reader sees it between the moment before it and the moment after.
/// </summary>
internal sealed record LostEntriesNotice(int Count);

/// <summary>
/// The Fast suite's stand-in for where a run's record is written. An in-memory adapter at an owned
/// port, which is the only kind of double this project uses (Constitution III.9); the real adapter is
/// <c>MarkdownRunRecord</c>, and the Contract suite drives that against a real filesystem.
/// </summary>
/// <remarks>
/// It keeps what it was given, in the order it was given it, so a test can ask what a record holds
/// and in which order — which is the whole of RUNS-007's ordering. It renders nothing: what the
/// Markdown looks like is <c>RecordText</c>'s, and that is a pure function a test drives directly.
/// <para>
/// <see cref="FailWrites"/> is what makes RUNS-007's "a write that fails is counted and does not
/// throw" reachable at all. A real disk cannot be made full on demand, which is why the counting
/// half is proven here and the real adapter's own IO failure in the Contract suite.
/// </para>
/// </remarks>
internal sealed class InMemoryRunRecord : IRunRecord
{
    private readonly Dictionary<Guid, List<object>> written = [];
    private readonly Dictionary<Guid, int> lost = [];

    /// <summary>What has been lost and not yet announced in the record.</summary>
    private readonly Dictionary<Guid, int> unannounced = [];
    private readonly Lock gate = new();

    /// <summary>
    /// While set, every call is counted as lost instead of written — an unwritable directory, a full
    /// disk. Nothing throws either way (RUNS-007).
    /// </summary>
    public bool FailWrites { get; set; }

    /// <summary>Whether this record was asked to write anything at all for that run.</summary>
    public bool Holds(Guid runId)
    {
        lock (gate)
        {
            return written.ContainsKey(runId);
        }
    }

    /// <summary>Everything written for that run, in the order it was written.</summary>
    public IReadOnlyList<object> Of(Guid runId)
    {
        lock (gate)
        {
            return written.TryGetValue(runId, out var entries) ? [.. entries] : [];
        }
    }

    /// <summary>The moments of that run, in order, with the frame left out.</summary>
    public IReadOnlyList<RunMoment> MomentsOf(Guid runId) => [.. Of(runId).OfType<RunMoment>()];

    /// <summary>That run's head, or null where none was written.</summary>
    public RunFrameHead? HeadOf(Guid runId) => Of(runId).OfType<RunFrameHead>().FirstOrDefault();

    /// <summary>That run's tail, or null while it is still under way.</summary>
    public RunFrameTail? TailOf(Guid runId) => Of(runId).OfType<RunFrameTail>().FirstOrDefault();

    public void Begin(RunFrameHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        Write(head.RunId, head);
    }

    public void Append(RunMoment moment)
    {
        ArgumentNullException.ThrowIfNull(moment);

        lock (gate)
        {
            // Nothing is written after the tail, the same promise the real adapter makes: a moment
            // already in flight when the run ended is dropped, and is not counted lost — no write
            // failed (contracts/run-record.md).
            if (HasATail(moment.RunId))
            {
                return;
            }

            Write(moment.RunId, moment);
        }
    }

    public void Ended(RunFrameTail tail)
    {
        ArgumentNullException.ThrowIfNull(tail);

        // One lock across the check and the write: read apart, two endings racing would both pass.
        lock (gate)
        {
            // A run ends once, so a second call appends nothing. And a tail that could not be written
            // is not a tail: it may still be written, exactly as the real adapter allows (RUNS-007).
            if (HasATail(tail.RunId))
            {
                return;
            }

            Write(tail.RunId, tail);
        }
    }

    /// <summary>Whether this run's tail is already in the record. Assumes the lock.</summary>
    private bool HasATail(Guid runId) =>
        written.TryGetValue(runId, out var entries) && entries.OfType<RunFrameTail>().Any();

    public int EntriesLost(Guid runId)
    {
        lock (gate)
        {
            return lost.GetValueOrDefault(runId);
        }
    }

    /// <summary>
    /// The record as bytes, rendered the way the real adapter renders it — through the same
    /// <c>RecordText</c>. A double that shaped the file differently from the adapter it stands in for
    /// would not merely miss a difference; it would hide one.
    /// </summary>
    public byte[]? Read(Guid runId)
    {
        var entries = Of(runId);

        return entries.Count == 0
            ? null
            : Encoding.UTF8.GetBytes(string.Concat(entries.Select(TextOf)));
    }

    private static string TextOf(object entry) => entry switch
    {
        RunFrameHead head => RecordText.Head(head),
        RunMoment moment => RecordText.Moment(moment),
        RunFrameTail tail => RecordText.Tail(tail),
        LostEntriesNotice notice => RecordText.EntriesLost(notice.Count, FastSuite.Start),
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "not something a record holds"),
    };

    private void Write(Guid runId, object entry)
    {
        lock (gate)
        {
            if (FailWrites)
            {
                lost[runId] = lost.GetValueOrDefault(runId) + 1;
                unannounced[runId] = unannounced.GetValueOrDefault(runId) + 1;
                return;
            }

            if (!written.TryGetValue(runId, out var entries))
            {
                entries = [];
                written[runId] = entries;
            }

            // The gap goes where it happened: before the entry that finally got through, so a reader
            // sees it between the moment before it and the moment after (RUNS-007).
            if (unannounced.GetValueOrDefault(runId) is > 0 and var missing)
            {
                entries.Add(new LostEntriesNotice(missing));
                unannounced[runId] = 0;
            }

            entries.Add(entry);
        }
    }
}

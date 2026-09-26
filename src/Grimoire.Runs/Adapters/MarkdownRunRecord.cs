using System.Globalization;

namespace Grimoire.Runs.Adapters;

/// <summary>
/// A run's record on disk: one Markdown file per run, appended to and never rewritten
/// (RUNS-007, contracts/run-record.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>The only place a record file is written</b> (Constitution V.2). The directory is Grimoire's
/// own — <c>runs/</c> inside the one <c>--state</c> names, beside <c>submissions.db</c> — and never
/// the wiki: Grimoire's bookkeeping in the user's repository would turn up in the version history
/// that is their only undo (DEC-023's reasoning, Invariants 1 and 3). <c>Program.cs</c> already
/// refuses a <c>--state</c> inside the wiki, so the records inherit that guard.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> An unwritable directory, a full disk, a file another program holds
/// open — each is caught and counted, and the run goes on. A throw would end the run, which is the
/// opposite of what was decided; a silence would leave an unwritten record indistinguishable from an
/// agent that did nothing, so the count travels with the run's figures and the record itself says
/// how many entries were lost once a write succeeds again (RUNS-007, research.md R-10).
/// </para>
/// <para>
/// Appended and never rewritten, which is also the cheapest write there is to make survive a stop —
/// the concern DEC-023 settled for the queue. Every call returns only once the change is on disk:
/// RUNS-004 covers a stop that gives Grimoire no chance to act, so nothing may be waiting to be
/// written.
/// </para>
/// </remarks>
public sealed class MarkdownRunRecord : IRunRecord
{
    private readonly string directory;

    /// <summary>How many entries of each run could not be written, in all.</summary>
    private readonly Dictionary<Guid, int> lost = [];

    /// <summary>What has been lost and not yet said so in the record itself.</summary>
    private readonly Dictionary<Guid, int> unannounced = [];

    /// <summary>The runs whose tail is already on disk. A run ends once.</summary>
    private readonly HashSet<Guid> ended = [];
    private readonly Lock gate = new();

    /// <summary>
    /// The records live in <c>runs/</c> inside the state directory. Creating it here rather than at
    /// the first write means a directory that cannot be made at all is found before any run begins —
    /// and where it still cannot, the head is simply the first entry lost and nothing special-cases
    /// it (research.md R-10).
    /// </summary>
    public MarkdownRunRecord(string stateDirectory)
    {
        directory = Path.Combine(stateDirectory, "runs");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Counted at the first write, against the run it belongs to. There is no run to count it
            // against here.
        }
    }

    public void Begin(RunFrameHead head)
    {
        ArgumentNullException.ThrowIfNull(head);

        Write(head.RunId, head.StartedAt, RecordText.Head(head));
    }

    public void Append(RunMoment moment)
    {
        ArgumentNullException.ThrowIfNull(moment);

        lock (gate)
        {
            // Nothing is written after the tail (contracts/run-record.md). Enforced here rather than
            // by whoever calls: the conductor looks a run up and appends in two steps, so a moment
            // already in flight can arrive after the ending that removed it — and this file is the one
            // place that knows whether the tail is on disk. Such a moment is dropped and not counted
            // lost: no write failed, the run was simply already over, which is how a late report is
            // already treated everywhere else.
            if (ended.Contains(moment.RunId))
            {
                return;
            }

            Write(moment.RunId, moment.At, RecordText.Moment(moment));
        }
    }

    public void Ended(RunFrameTail tail)
    {
        ArgumentNullException.ThrowIfNull(tail);

        lock (gate)
        {
            // A run ends once, so a second call appends nothing.
            if (ended.Contains(tail.RunId))
            {
                return;
            }

            // Marked ended only once the tail is actually on disk. Marked before, a tail lost to a
            // full disk could never be written at all, and the record would be missing its tail with
            // nothing able to put one there (RUNS-007).
            if (Write(tail.RunId, tail.EndedAt, RecordText.Tail(tail)))
            {
                ended.Add(tail.RunId);
            }
        }
    }

    public int EntriesLost(Guid runId)
    {
        lock (gate)
        {
            return lost.GetValueOrDefault(runId);
        }
    }

    public byte[]? Read(Guid runId)
    {
        try
        {
            return File.ReadAllBytes(PathOf(runId));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No record, or one this process cannot read. Either way there is nothing to serve, and
            // the endpoint answers 404 — a record that was never written at all is one of the three
            // cases it already has that answer for (contracts/hub-http-api.md).
            return null;
        }
    }

    /// <summary>
    /// One append, committed before this returns, with the gap that preceded it put where it
    /// happened. Answers whether it reached the disk.
    /// </summary>
    /// <remarks>
    /// Under one lock: two moments of one run arrive on whatever thread the harness reads on, and
    /// two appends racing would interleave halves of two segments — a record no reader could
    /// segment. The lock is held across the whole write for the same reason.
    /// </remarks>
    private bool Write(Guid runId, DateTimeOffset at, string text)
    {
        lock (gate)
        {
            var missing = unannounced.GetValueOrDefault(runId);

            // The gap goes in front of the entry that finally got through, so a reader sees it
            // between the moment before it and the moment after (RUNS-007).
            var whole = missing > 0 ? RecordText.EntriesLost(missing, at) + text : text;

            try
            {
                File.AppendAllText(PathOf(runId), whole);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The run goes on. One more entry that is not in the record, and the count travels
                // with the run's figures until a write succeeds again.
                lost[runId] = lost.GetValueOrDefault(runId) + 1;
                unannounced[runId] = missing + 1;
                return false;
            }

            unannounced[runId] = 0;
            return true;
        }
    }

    /// <summary>
    /// <c>&lt;state&gt;/runs/&lt;runId&gt;.md</c>. A run's identifier is a fresh GUID, so two
    /// records cannot collide and none has to be checked for.
    /// </summary>
    private string PathOf(Guid runId) =>
        Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{runId}.md"));
}

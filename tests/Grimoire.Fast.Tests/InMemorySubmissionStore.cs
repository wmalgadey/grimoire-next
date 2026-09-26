using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What happened, in the order it happened, across both doubles at once.
/// </summary>
/// <remarks>
/// RUNS-006 makes an <em>order</em> observable — an agent terminated before its run reads failed
/// and before anything else starts — and no single double can show it: the terminating is the
/// harness's and the state is the store's. One journal both write to is what puts them on one
/// timeline.
/// </remarks>
internal sealed class HubJournal
{
    private readonly List<string> entries = [];
    private readonly Lock gate = new();

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
    }

    public void Record(string what)
    {
        lock (gate)
        {
            entries.Add(what);
        }
    }

    /// <summary>Where this happened on the timeline, or -1 where it did not.</summary>
    public int When(string what) => Entries.ToList().IndexOf(what);
}

/// <summary>
/// The Fast suite's stand-in for where submissions live. An in-memory adapter at an owned port,
/// which is the only kind of double this project uses (Constitution III.9); the real adapter is
/// <c>SqliteSubmissionStore</c>, and the Contract suite drives that against a real file.
/// </summary>
/// <remarks>
/// It keeps rows, not objects: what <see cref="Load"/> hands back is built afresh, so a Fast test
/// that restarts the hub over this store gets what a second process would get, and cannot pass by
/// sharing an object with the board it came from.
/// <para>
/// It keeps them in the order they were added, which is the order the port promises and the order
/// the real adapter reads back (by `rowid`). Sorted by `SubmittedAt` instead, a Fast restart would
/// quietly reorder the queue where a clock correction had moved a stamp — and hide the very bug
/// the real adapter is written to avoid.
/// </para>
/// </remarks>
internal sealed class InMemorySubmissionStore(HubJournal? journal = null) : ISubmissionStore
{
    private readonly List<Guid> accepted = [];
    private readonly Dictionary<Guid, StoredSubmission> held = [];
    private readonly Dictionary<Guid, StoredRun> runs = [];
    private readonly Lock gate = new();

    public IReadOnlyList<StoredSubmission> Load()
    {
        lock (gate)
        {
            return
            [
                .. accepted
                    .Select(id => held[id])
                    .Select(s => s with { Run = s.Run is null ? null : runs[s.Run.Id] }),
            ];
        }
    }

    public void Add(StoredSubmission submission)
    {
        lock (gate)
        {
            accepted.Add(submission.Id);
            held[submission.Id] = submission;
            journal?.Record($"added {submission.Id}");
        }
    }

    public void AssignRun(Guid submissionId, StoredRun run)
    {
        lock (gate)
        {
            runs[run.Id] = run;
            held[submissionId] = held[submissionId] with { Run = run };
            journal?.Record($"handed out {submissionId}");
        }
    }

    public void RecordAgentProcess(Guid runId, AgentProcessIdentity identity)
    {
        lock (gate)
        {
            runs[runId] = runs[runId] with { AgentProcess = identity };
            journal?.Record($"agent of {runId} is {identity.ProcessId}");
        }
    }

    public void SetState(Guid submissionId, SubmissionState state)
    {
        lock (gate)
        {
            held[submissionId] = held[submissionId] with { State = state };
            journal?.Record($"{submissionId} reads {state}");
        }
    }

    public void RecordFigures(Guid runId, long tokensUsed, int toolCalls, int entriesLost)
    {
        lock (gate)
        {
            runs[runId] = runs[runId] with
            {
                TokensUsed = tokensUsed,
                ToolCalls = toolCalls,
                EntriesLost = entriesLost,
            };

            // Journalled, because "written only where a figure has actually risen" is a claim about
            // how often this is reached and not only about what it leaves behind (research.md R-06).
            journal?.Record($"figures of {runId} are {tokensUsed}/{toolCalls}/{entriesLost}");
        }
    }

    public void Acknowledge(Guid submissionId, DateTimeOffset at)
    {
        lock (gate)
        {
            held[submissionId] = held[submissionId] with { AcknowledgedAt = at };
            journal?.Record($"acknowledged {submissionId}");
        }
    }
}

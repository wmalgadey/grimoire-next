using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// A run as what survives a stop keeps it: its identifier, the submission it works, when it began,
/// the tools it was granted, the model it ran on, which process its agent is, and its figures.
/// </summary>
/// <param name="Model">
/// The pinned model id this run ran on. Kept because a record from last month must say which model
/// served it, and the owner may change <c>--model</c> between runs (RUNS-008, DEC-010).
/// </param>
/// <param name="TokensUsed">
/// Every token the run has caused. Kept because RUNS-010 makes the figures survive a stop: OUT-02 has
/// the user read a failed run's cost, so the number now has a consumer (Constitution II.1).
/// </param>
/// <param name="ToolCalls">How many tool calls the run made, for the same reason (RUNS-010).</param>
/// <param name="EntriesLost">
/// How many entries this run's record could not hold (RUNS-007). Kept so that the gap is visible after
/// a restart too: a record that could not be written must not look like a run that did nothing.
/// </param>
/// <param name="GrantedTools">
/// The bare tool names this run was given, in order. Kept because GUARD-003 says the grant is
/// recorded for every run, and once RUNS-004 makes the submission outlive the process a grant that
/// still vanished would leave that record weaker than the state around it (research.md R-08).
/// </param>
/// <param name="AgentProcess">
/// Which process this run's agent is, or null until the child exists. Read once, at start-up, to
/// terminate an agent that outlived a stop Grimoire could not act on (RUNS-006).
/// </param>
public sealed record StoredRun(
    Guid Id,
    Guid SubmissionId,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> GrantedTools,
    DateTimeOffset GrantRecordedAt,
    string Model,
    AgentProcessIdentity? AgentProcess,
    long TokensUsed = 0,
    int ToolCalls = 0,
    int EntriesLost = 0)
{
    /// <summary>
    /// A run as it is at the moment it is handed out: nothing spent, nothing called, nothing lost.
    /// </summary>
    /// <remarks>
    /// <c>002-ingest-queue</c> said the tokens a run spent were "deliberately not here", because a
    /// number nothing reads would be a placeholder for later (Constitution II.1). That assumption is
    /// <b>withdrawn</b>: OUT-02 has the user read a failed run's cost, so all three figures now have a
    /// consumer and survive a stop with the run (RUNS-010, research.md R-06).
    /// </remarks>
    public static StoredRun Of(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new StoredRun(
            run.Id,
            run.SubmissionId,
            run.StartedAt,
            run.Grant.ToolNames,
            run.Grant.RecordedAt,
            run.Model,
            AgentProcess: null);
    }
}

/// <summary>
/// One submission as the store holds it: facts, with no judgment on them. What they mean for the
/// queue is <see cref="SubmissionBoard.Restore"/>'s.
/// </summary>
public sealed record StoredSubmission(
    Guid Id,
    string Text,
    DateTimeOffset SubmittedAt,
    SubmissionState State,
    StoredRun? Run,
    DateTimeOffset? AcknowledgedAt)
{
    /// <summary>
    /// The run was in progress when Grimoire stopped: it had been handed out and had not ended.
    /// Its agent is terminated where it is still alive, and then it reads failed — in that order,
    /// and before anything else starts (RUNS-006, RUNS-004).
    /// </summary>
    public bool WasUnderWay => Run is not null && State is not (SubmissionState.Done or SubmissionState.Failed);
}

/// <summary>
/// Where the submissions live so that they survive Grimoire stopping (RUNS-004). The RUNS context's
/// first port, and the third external system in the tree; its adapter is
/// <c>SqliteSubmissionStore</c>, and the Fast suite has an in-memory one at the same port
/// (Constitution II.4, III.9, V.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every call returns only once the change is on disk.</b> There is no flush, no save and no
/// write at close: RUNS-004 covers a stop that gives Grimoire no chance to act — a kill, a power
/// cut — so nothing may be waiting to be written (contracts/submission-store.md, research.md R-02).
/// </para>
/// <para>
/// The members are synchronous, unlike every other port here. Two reasons, and neither is
/// convenience: the board writes each change under the one lock it decides the queue rule with,
/// and a lock cannot be held across an await; and the store is a local file reached through a
/// provider whose own writes are synchronous, so an async signature would promise a yielding call
/// that never yields.
/// </para>
/// <para>
/// <b>No delete, and no way to change a submission's text.</b> The same shape
/// <see cref="Grimoire.Wiki.IWikiStore"/> has and for the same reason: nothing in this feature
/// needs one, and cancelling a submission is out of scope.
/// </para>
/// <para>
/// The store judges nothing. Whether a run may start, whether a failure blocks, which submission is
/// next — all of that is the board's (RUNS-002, RUNS-003). This keeps facts and hands them back.
/// </para>
/// </remarks>
public interface ISubmissionStore
{
    /// <summary>
    /// Everything the store holds, oldest first — the queue's order. Read once, as the hub starts
    /// and before anything is served.
    /// </summary>
    IReadOnlyList<StoredSubmission> Load();

    /// <summary>
    /// A text was accepted. Called <b>before the user is answered</b>, so that a user who is told
    /// their submission was accepted and then loses power finds it after the restart (RUNS-004).
    /// </summary>
    void Add(StoredSubmission submission);

    /// <summary>
    /// The board has handed this submission out: the run's identifier onto the submission, and the
    /// run's own record beside it.
    /// </summary>
    void AssignRun(Guid submissionId, StoredRun run);

    /// <summary>
    /// The agent's child process exists. Written as soon as it does, because a kill a moment later
    /// is exactly the case it is for (RUNS-006).
    /// </summary>
    void RecordAgentProcess(Guid runId, AgentProcessIdentity identity);

    /// <summary>
    /// The submission's state. The transition is not checked here — RUNS-001 is the board's.
    /// </summary>
    void SetState(Guid submissionId, SubmissionState state);

    /// <summary>The user has acknowledged this submission's failed run (RUNS-003).</summary>
    void Acknowledge(Guid submissionId, DateTimeOffset at);

    /// <summary>
    /// What the run has spent, how many calls it has made, and how many entries its record could not
    /// hold (RUNS-010, RUNS-007).
    /// </summary>
    /// <remarks>
    /// One member for the three, because one event writes them and one row reads them: written
    /// separately, a restart could come back with a token figure from one moment beside a call count
    /// from another. Called only where a figure has actually risen — <c>Run.Spent</c> is already a
    /// <c>Math.Max</c>, so the store sees two to four writes a turn rather than the sixty streamed
    /// lines a turn carries (research.md R-06).
    /// </remarks>
    void RecordFigures(Guid runId, long tokensUsed, int toolCalls, int entriesLost);
}

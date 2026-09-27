using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// A run as what survives a stop keeps it: its identifier, what caused it, when it began, the tools
/// it was granted, the model it ran on, which process its agent is, and its figures.
/// </summary>
/// <param name="QueuedId">
/// The submission <b>or the question</b> that caused this run, and <c>null</c> where a question did.
/// One name for one thing, now that two kinds cause runs.
/// <para>
/// Null rather than the question's own identifier, because nothing of a question is on disk
/// (QUERY-005) and an id pointing at something that was never written would be a reference that
/// cannot be followed. What the row is for is RUNS-006: a start-up has to terminate the agent of
/// every run it reads as having been in progress, and a question's run with nothing on disk would
/// leave an orphaned <c>claude</c> holding the granted tools with no ceiling on it (research.md R-04).
/// </para>
/// </param>
/// <param name="Model">
/// The pinned model id this run ran on. Kept because a record from last month must say which model
/// served it, and the owner may change <c>--model</c> between runs (RUNS-008, DEC-010).
/// </param>
/// <param name="CostSpent">
/// What the run has cost, in input-token equivalents. Kept because RUNS-010 makes the figures survive
/// a stop: OUT-02 has the user read a failed run's cost, so the number now has a consumer
/// (Constitution II.1).
/// </param>
/// <param name="Tokens">
/// The four raw counts behind <see cref="CostSpent"/>: input, output, cache read, cache write. Kept
/// because the weighting cannot be undone, and the placeholder the cost ceiling stands at is
/// calibrated from what real runs actually caused (GUARD-004).
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
    Guid? QueuedId,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> GrantedTools,
    DateTimeOffset GrantRecordedAt,
    string Model,
    AgentProcessIdentity? AgentProcess,
    long CostSpent = 0,
    ModelTokens Tokens = default,
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

            // A question's run carries no id here: the question is on no disk, so there is nothing
            // for one to point at (QUERY-005, research.md R-04).
            run.CausedBy is RunCause.AQuestion ? null : run.QueuedId,
            run.StartedAt,
            run.Grant.ToolNames,
            run.Grant.RecordedAt,
            run.Model,
            AgentProcess: null);
    }
}

/// <summary>
/// One submission as the store holds it: facts, with no judgment on them. What they mean for the
/// queue is <see cref="RunBoard.Restore"/>'s.
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
    /// A run with <b>no submission behind it</b> — one a question caused (RUNS-006, RUNS-010).
    /// </summary>
    /// <remarks>
    /// Written the moment the board hands the question out, for the same reason
    /// <see cref="AssignRun"/> is written before the submission is marked: a write that fails must
    /// leave the queue as it was. There is no second statement beside it, because there is no
    /// submission row to point at the run.
    /// </remarks>
    void AddRun(StoredRun run);

    /// <summary>
    /// The runs with no submission behind them that were in progress when Grimoire stopped, read at
    /// start-up beside <see cref="Load"/> (RUNS-004, RUNS-006).
    /// </summary>
    /// <remarks>
    /// In progress means: no submission, and no ending written for it. A start-up terminates the
    /// agent of each where the recorded identity is still live and then marks the run ended failed.
    /// <b>No tail is written</b> — such a run has no record (RUNS-007) — and nothing is restored into
    /// a chat, because QUERY-005 empties it.
    /// </remarks>
    IReadOnlyList<StoredRun> LoadRunsWithoutASubmission();

    /// <summary>
    /// A run ended and there is <b>no submission state to set</b> — one a question caused. Its final
    /// figures, keyed by the run (RUNS-010).
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Ended"/> rather than folded into it: that member's promise is that the
    /// terminal state and the figures are <em>one</em> change, and there is no state here to make one
    /// of. What it shares with <see cref="RecordFigures"/> is the statement and not the meaning — this
    /// is the run's last word, and it is also what marks the run as no longer in progress, so a
    /// start-up does not read it back as one to terminate.
    /// </remarks>
    void RunEnded(Guid runId, long costSpent, ModelTokens tokens, int toolCalls, int entriesLost);

    /// <summary>
    /// The agent's child process exists. Written as soon as it does, because a kill a moment later
    /// is exactly the case it is for (RUNS-006).
    /// </summary>
    void RecordAgentProcess(Guid runId, AgentProcessIdentity identity);

    /// <summary>
    /// The submission's state. The transition is not checked here — RUNS-001 is the board's.
    /// </summary>
    void SetState(Guid submissionId, SubmissionState state);

    /// <summary>
    /// The run ended: its submission's terminal state and the run's final figures, <b>in one
    /// change</b> (RUNS-001, RUNS-010).
    /// </summary>
    /// <remarks>
    /// Not <see cref="SetState"/> followed by <see cref="RecordFigures"/>. Those are two changes, and
    /// a stop between them — which RUNS-004 covers, a kill or a power cut — would leave a submission
    /// that reads done or failed beside the figures it had one moment earlier. RUNS-010 has the
    /// figures stand as the run's final ones once it has ended <em>and</em> survive a stop, and two
    /// changes cannot promise both.
    /// </remarks>
    void Ended(
        Guid submissionId,
        SubmissionState terminal,
        Guid runId,
        long costSpent,
        ModelTokens tokens,
        int toolCalls,
        int entriesLost);

    /// <summary>The user has acknowledged this submission's failed run (RUNS-003).</summary>
    void Acknowledge(Guid submissionId, DateTimeOffset at);

    /// <summary>
    /// What the run has spent, how many calls it has made, and how many entries its record could not
    /// hold (RUNS-010, RUNS-007).
    /// </summary>
    /// <remarks>
    /// One member for the three, because one event writes them and one row reads them: written
    /// separately, a restart could come back with a cost figure from one moment beside a call count
    /// from another. Called only where a figure has actually risen — <c>Run.Spent</c> is already a
    /// <c>Math.Max</c>, so the store sees two to four writes a turn rather than the sixty streamed
    /// lines a turn carries (research.md R-06).
    /// </remarks>
    void RecordFigures(Guid runId, long costSpent, ModelTokens tokens, int toolCalls, int entriesLost);
}

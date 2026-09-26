namespace Grimoire.Agent;

/// <summary>
/// Which process a run's agent is: its identifier <em>and</em> the moment that process started.
/// </summary>
/// <remarks>
/// The pair, never the identifier alone. Operating systems reuse those numbers, and after a reboot
/// one almost certainly belongs to something else — terminating it would kill an unrelated program
/// on the owner's machine, which is the one failure in this feature that does damage outside
/// Grimoire. Two processes sharing an identifier <em>and</em> a start time to the tick do not
/// occur, and a reboot changes every start time, so the pair also handles the reboot case with no
/// rule of its own (RUNS-006, research.md R-11).
/// </remarks>
public sealed record AgentProcessIdentity(int ProcessId, DateTimeOffset StartedAt);

/// <summary>How a run ended, as the hub decides it. The harness reports; it decides nothing.</summary>
public enum RunOutcome
{
    Done,
    Failed,
}

/// <summary>
/// Why a run ended, as the record's tail states it. Seven values, one requirement — the values a
/// requirement covers are a list inside it and never one requirement per value (RUNS-008,
/// Constitution IV.7). Each is a state the code already reaches; none is new behaviour.
/// </summary>
/// <remarks>
/// Beside <see cref="RunOutcome"/> rather than in <c>Grimoire.Runs/IRunRecord.cs</c>, where
/// data-model.md puts it, for the reason that file's own header gives for reading GUARD's
/// vocabulary: RUNS references this project and nothing goes the other way, and the reason travels
/// out of the harness through <see cref="RunReport.RunEnded"/> exactly as the outcome does. A run
/// ending for a reason only the adapter knows — a tool surface that was not the grant — cannot be
/// inferred by the hub from anything else it holds, which is why it is reported rather than derived.
/// </remarks>
public enum RunEndedBecause
{
    /// <summary>The agent stopped inside both ceilings with its log entry present (RUNS-005).</summary>
    StoppedWithItsLogEntry,

    /// <summary>It stopped without that entry, after being told once (RUNS-005).</summary>
    StoppedWithoutItsLogEntry,

    /// <summary>The elapsed ceiling (GUARD-004).</summary>
    TimeCeiling,

    /// <summary>The cost ceiling (GUARD-004).</summary>
    CostCeiling,

    /// <summary>What <c>system/init</c> reported was not the grant (GUARD-001).</summary>
    ToolsWereNotTheGrant,

    /// <summary>A non-zero exit, a dispatch that never started a process, or a reader that lost one.</summary>
    AgentProcessDied,

    /// <summary>Grimoire was stopped while the run was in progress (RUNS-004, RUNS-006).</summary>
    GrimoireStopped,
}

/// <summary>Which of the four things a moment of a run's record is (RUNS-009).</summary>
public enum RunMomentKind
{
    /// <summary>A tool call: the tool's name, and its arguments as the content.</summary>
    ToolCalled,

    /// <summary>What a call returned, whole.</summary>
    ToolReturned,

    /// <summary>The agent's own text.</summary>
    AgentSaid,

    /// <summary>What Grimoire told the agent — today only the nudge.</summary>
    GrimoireSaid,
}

/// <summary>
/// One thing the transcript saw, as the adapter reports it.
/// </summary>
/// <param name="Tool">The tool's name for the two tool kinds, and null for the other two.</param>
/// <param name="Content">
/// The arguments, the result, or the text. <b>Null means the content could not be read</b> — a
/// <c>tool_result</c> whose <c>content</c> is neither a string nor an array of blocks. Such a moment
/// is recorded as a result that could not be read and never dropped: what cannot be read is refused
/// rather than read around, the way a <c>tools</c> array that is not names already is (GUARD-001's
/// precedent, data-model.md §RunMoment).
/// </param>
public sealed record TranscriptMoment(RunMomentKind Kind, string? Tool, string? Content);

/// <summary>
/// What a run is given, and what it is allowed to reach.
/// </summary>
/// <param name="RunId">
/// Names the run in the wiki's log, where RUNS-005 matches on it, and addresses the run's own tool
/// endpoint.
/// </param>
/// <param name="Prompt">
/// The whole of what reaches the agent: instruction, purpose description, run identifier and the
/// submitted text. Assembled by the hub's instruction loader and by nothing else (Constitution V.1).
/// </param>
/// <param name="Grant">The run's entire tool surface (GUARD-001).</param>
/// <param name="Model">
/// A pinned model id, never an alias and never the default. A run is reproducible and its cost
/// attributable only if the model is fixed (research.md R-11).
/// </param>
public sealed record AgentDispatch(Guid RunId, Guid SubmissionId, string Prompt, ToolGrant Grant, string Model);

/// <summary>
/// How a run reports back while it is under way, keyed by the submission it belongs to.
/// </summary>
/// <remarks>
/// Delegates rather than an interface: an interface exists only at a port to something outside the
/// process (Constitution II.4), and these are calls back into the hub, which owns every judgment
/// about a run. The harness reports facts and acts on what it is told; it decides nothing.
/// </remarks>
/// <param name="AgentReportedIn">
/// The agent has reported in — <c>system/init</c>, with a tool surface that is the grant. A
/// submission reads <c>submitted</c> until this and <c>running</c> from then on.
/// </param>
/// <param name="CostSoFar">
/// Every token the run has caused so far, and the breakdown per model where the line carried one.
/// The hub watches the total against the cost ceiling and stops the run itself where it is reached
/// (GUARD-004); the breakdown is what the record's tail says the run spent per model (RUNS-008).
/// The two travel together because the total is the sum of the breakdown: reported apart, a tail
/// could name models adding up to a figure beside them that they do not add up to. Empty for a
/// streamed line, which carries no breakdown.
/// </param>
/// <param name="MomentHappened">
/// One thing the run did — a tool call, what it returned, or the agent's own text (RUNS-009). The
/// one delegate this feature adds: all four kinds of moment are one thing happening, so one
/// delegate carries them, and the fourth kind is the hub's own and never arrives here.
/// </param>
/// <param name="AgentStopped">
/// The agent has stopped — a <c>result</c>. The hub reads the wiki's log and decides whether to
/// nudge (RUNS-005). It does <b>not</b> end the run here; what this settles is only whether
/// anything further is sent. The flag says whether the agent stopped of its own accord.
/// </param>
/// <param name="AgentExited">
/// The run's process is gone, with this exit code. This is where a run ends: the result, the log
/// entry and the exit code are read together, and all three have to agree for a run to be done
/// (contracts/agent-cli-protocol.md, RUNS-005, GUARD-004).
/// </param>
/// <param name="RunEnded">
/// The run is over without a process exit to read — a dispatch that never started one, or a
/// surface refused before the first model call — and why, which the caller knows and the hub cannot
/// derive: those two endings are alike in every other fact the hub holds.
/// </param>
/// <param name="AgentProcessIs">
/// Which process this run's agent is, reported as soon as the child exists and before anything is
/// written to it. That moment is the point: a kill an instant later is the case the identity is
/// kept for, and one that was never written down cannot be found again (RUNS-006).
/// </param>
public sealed record RunReport(
    Action<Guid> AgentReportedIn,
    Action<Guid, long, IReadOnlyDictionary<string, ModelTokens>> CostSoFar,
    Func<Guid, bool, Task> AgentStopped,
    Action<Guid, int> AgentExited,
    Action<Guid, RunOutcome, RunEndedBecause> RunEnded,
    Action<Guid, AgentProcessIdentity> AgentProcessIs,
    Action<Guid, TranscriptMoment> MomentHappened);

/// <summary>
/// The port to the agent. Its one adapter is <c>HarnessProcess</c>, which with <c>AgentTranscript</c>
/// beside it is the only place the <c>claude</c> process and its protocol appear (Constitution
/// V.2); the Fast suite uses an in-memory adapter at this same port (III.9).
/// </summary>
public interface IAgentHarness
{
    /// <summary>
    /// The one thing Grimoire ever says to a running agent: that the wiki's log holds no entry for
    /// its run (RUNS-005).
    /// </summary>
    /// <remarks>
    /// Declared at the port rather than inside the adapter, because two places need the same words:
    /// the adapter writes them to the agent, and the hub records them in the run's record as what
    /// Grimoire said (RUNS-009). One constant, so the record cannot say something other than what was
    /// sent. This is not the prompt, which <c>InstructionLoader</c> alone assembles (Constitution
    /// V.1).
    /// </remarks>
    const string LogEntryMissing = "No log entry for this run was found in log.md.";

    /// <summary>
    /// Start a run. Returns once the run is under way — <em>not</em> once it has ended, and not
    /// once the agent has reported in: the user is not made to wait for either (INGEST-001).
    /// </summary>
    Task DispatchAsync(AgentDispatch dispatch, RunReport report, CancellationToken cancellationToken);

    /// <summary>
    /// Tell a running agent, once, that the wiki's log holds no entry for its run, and let it
    /// continue inside the same ceilings (RUNS-005).
    /// </summary>
    Task NudgeAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Stop a run at once, a model call in flight included. Sent at either ceiling (GUARD-004).
    /// </summary>
    Task StopAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Tell a run that nothing further is coming, and let its process end of its own accord. The
    /// CLI reads stdin for as long as it is open, so this is what lets a finished agent exit at
    /// all (contracts/agent-cli-protocol.md).
    /// </summary>
    /// <remarks>
    /// This does not end the run and does not wait for the exit. The run ends when the process is
    /// gone and its exit code can be read, which is reported through
    /// <see cref="RunReport.AgentExited"/>.
    /// </remarks>
    Task NothingFurtherAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// End a process that outlived a stop Grimoire could not act on — but only where it is still
    /// that run's agent (RUNS-006).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called at start-up, for every run read as having been in progress, before that run reads
    /// failed and before any further run starts. Both halves of the identity must match a live
    /// process: an identifier alone is not an identity, and acting on one that has been reused
    /// would kill an unrelated program on the owner's machine (research.md R-11).
    /// </para>
    /// <para>
    /// A process that is already gone, or that carries the identifier but not the start time, is
    /// left alone and is not an error. Synchronous because it is one act on the operating system,
    /// and because it happens before the hub serves anything.
    /// </para>
    /// </remarks>
    void Terminate(AgentProcessIdentity identity);
}

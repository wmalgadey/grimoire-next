namespace Grimoire.Agent;

/// <summary>How a run ended, as the hub decides it. The harness reports; it decides nothing.</summary>
public enum RunOutcome
{
    Done,
    Failed,
}

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
/// Every token the run has caused so far. The hub watches it against the cost ceiling and stops
/// the run itself where it is reached (GUARD-004).
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
/// surface refused before the first model call.
/// </param>
public sealed record RunReport(
    Action<Guid> AgentReportedIn,
    Action<Guid, long> CostSoFar,
    Func<Guid, bool, Task> AgentStopped,
    Action<Guid, int> AgentExited,
    Action<Guid, RunOutcome> RunEnded);

/// <summary>
/// The port to the agent. Its one adapter is <c>HarnessProcess</c>, which with <c>AgentTranscript</c>
/// beside it is the only place the <c>claude</c> process and its protocol appear (Constitution
/// V.2); the Fast suite uses an in-memory adapter at this same port (III.9).
/// </summary>
public interface IAgentHarness
{
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
}

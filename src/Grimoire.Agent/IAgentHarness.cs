namespace Grimoire.Agent;

/// <summary>How a run ended, as the harness reports it. The hub decides which of the two it is.</summary>
public enum RunOutcome
{
    Done,
    Failed,
}

/// <summary>
/// What a run is given. The run's own identifier travels with it: it names the run in the
/// generation record (WIKI-002) and in the log entry RUNS-005 matches on, and it addresses the
/// run's tool endpoint.
/// </summary>
public sealed record AgentDispatch(Guid RunId, Guid SubmissionId, string Text);

/// <summary>
/// How a run reports back while it is under way, keyed by the submission it belongs to.
/// </summary>
/// <remarks>
/// Delegates rather than an interface: an interface exists only at a port to something outside the
/// process (Constitution II.4), and these are calls back into the hub, which owns every judgment
/// about a run. The harness reports; it decides nothing.
/// </remarks>
/// <param name="AgentReportedIn">
/// The agent has reported in — <c>system/init</c>. A submission reads <c>submitted</c> until this
/// and <c>running</c> from then on.
/// </param>
/// <param name="RunEnded">The run is over, one way or the other.</param>
public sealed record RunReport(Action<Guid> AgentReportedIn, Action<Guid, RunOutcome> RunEnded);

/// <summary>
/// The port to the agent. Its one adapter is <c>HarnessProcess</c>, which is the only place the
/// <c>claude</c> process and its protocol appear (Constitution V.2); the Fast suite uses an
/// in-memory adapter at this same port (III.9).
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
}

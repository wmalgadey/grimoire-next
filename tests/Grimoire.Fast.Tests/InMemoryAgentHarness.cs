using Grimoire.Agent;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The Fast suite's stand-in for the agent. An in-memory adapter at an owned port, which is the
/// only kind of double this project uses (Constitution III.9); the real adapter is
/// <c>HarnessProcess</c>, and the Contract suite drives that against the real CLI.
/// </summary>
/// <remarks>
/// It reports nothing on its own. A test that wants the agent to report in, to spend, or to stop
/// says so itself, which is what keeps a run's timing out of the suite's 15 s budget. The one
/// judgment it does make is the grant check, and it makes it with the real
/// <see cref="ToolGrant.IsTheSurface"/> — a double that decided that for itself would prove
/// nothing.
/// </remarks>
internal sealed class InMemoryAgentHarness(HubJournal? journal = null) : IAgentHarness
{
    private readonly List<AgentProcessIdentity> terminated = [];
    private readonly List<AgentDispatch> dispatched = [];
    private readonly List<Guid> nudged = [];
    private readonly List<Guid> stopped = [];
    private readonly List<Guid> toldNothingFurther = [];
    private readonly Dictionary<Guid, RunReport> reports = [];

    /// <summary>Every dispatch this harness was given, in the order it was given them.</summary>
    public IReadOnlyList<AgentDispatch> Dispatched => dispatched;

    /// <summary>The agents this harness was asked to terminate at start-up (RUNS-006).</summary>
    public IReadOnlyList<AgentProcessIdentity> Terminated => terminated;

    /// <summary>
    /// Which process a dispatched run's agent is, as the real adapter reports it the moment the
    /// child exists. Left unset, no child is reported and no identity is recorded.
    /// </summary>
    public AgentProcessIdentity? AgentProcess { get; set; }

    public IReadOnlyList<Guid> Nudged => nudged;

    public IReadOnlyList<Guid> Stopped => stopped;

    /// <summary>The runs the hub has told that nothing further is coming.</summary>
    public IReadOnlyList<Guid> ToldNothingFurther => toldNothingFurther;

    /// <summary>
    /// What the run's process exits with once its stdin is closed. Zero unless a test says
    /// otherwise — a CLI that reports a clean result and then exits non-zero is one of the things
    /// the protocol's decision table has an answer for.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>A run is under way once it has been dispatched and has not yet reported an end.</summary>
    public bool RunUnderWay { get; private set; }

    /// <summary>Whether the agent ever reported in — the moment before its first model call.</summary>
    public bool ReportedIn { get; private set; }

    /// <summary>Set to make the next dispatch fail, as a process that will not start does.</summary>
    public Exception? DispatchFailure { get; set; }

    /// <summary>
    /// The tool surface the agent claims at <c>system/init</c>. Left unset, the agent is taken to
    /// report the grant it was given.
    /// </summary>
    public IReadOnlyList<string>? ReportedSurface { get; set; }

    public Task DispatchAsync(AgentDispatch dispatch, RunReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(report);

        reports[dispatch.SubmissionId] = report;

        if (DispatchFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        dispatched.Add(dispatch);
        RunUnderWay = true;
        journal?.Record($"dispatched {dispatch.SubmissionId}");

        if (AgentProcess is { } identity)
        {
            report.AgentProcessIs(dispatch.SubmissionId, identity);
        }

        if (ReportedSurface is { } surface && !dispatch.Grant.IsTheSurface(surface))
        {
            // A surface that is not the grant ends the run failed here, before any model call:
            // the agent never reports in (GUARD-001).
            RunUnderWay = false;
            report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// An agent that outlived a stop. Recorded rather than acted on: that a real process actually
    /// dies is the Contract suite's, against a real one (research.md R-11).
    /// </summary>
    public void Terminate(AgentProcessIdentity identity)
    {
        terminated.Add(identity);
        journal?.Record($"terminated {identity.ProcessId}");
    }

    public Task NudgeAsync(Guid runId, CancellationToken cancellationToken)
    {
        nudged.Add(runId);
        return Task.CompletedTask;
    }

    public Task StopAsync(Guid runId, CancellationToken cancellationToken)
    {
        stopped.Add(runId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Closing the run's stdin, as the real adapter does. The process then ends, and the run ends
    /// with it — which is why this reports the exit rather than only recording the call.
    /// </summary>
    public Task NothingFurtherAsync(Guid runId, CancellationToken cancellationToken)
    {
        toldNothingFurther.Add(runId);

        if (dispatched.FirstOrDefault(d => d.RunId == runId) is { } dispatch)
        {
            Exit(dispatch.SubmissionId, ExitCode);
        }

        return Task.CompletedTask;
    }

    /// <summary>The run's process is gone, with this exit code. Where a run ends.</summary>
    public void Exit(Guid submissionId, int exitCode)
    {
        RunUnderWay = false;
        reports[submissionId].AgentExited(submissionId, exitCode);
    }

    /// <summary>What the CLI's <c>system/init</c> does to the run.</summary>
    public void ReportIn(Guid submissionId)
    {
        ReportedIn = true;
        reports[submissionId].AgentReportedIn(submissionId);
    }

    /// <summary>What the streamed usage of a turn does: the run has spent this much so far.</summary>
    public void Spend(Guid submissionId, long tokensUsed) => reports[submissionId].CostSoFar(submissionId, tokensUsed);

    /// <summary>What the CLI's <c>result</c> message does: the agent has stopped, and the hub decides.</summary>
    public Task StoppedAsync(Guid submissionId, bool endedAbnormally = false) =>
        reports[submissionId].AgentStopped(submissionId, endedAbnormally);

    /// <summary>An ending the hub reaches without asking — a dispatch that never began, say.</summary>
    public void End(Guid submissionId, RunOutcome outcome)
    {
        RunUnderWay = false;
        reports[submissionId].RunEnded(submissionId, outcome);
    }
}

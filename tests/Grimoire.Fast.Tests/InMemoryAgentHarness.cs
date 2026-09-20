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
internal sealed class InMemoryAgentHarness : IAgentHarness
{
    private readonly List<AgentDispatch> dispatched = [];
    private readonly List<Guid> nudged = [];
    private readonly List<Guid> stopped = [];
    private readonly Dictionary<Guid, RunReport> reports = [];

    /// <summary>Every dispatch this harness was given, in the order it was given them.</summary>
    public IReadOnlyList<AgentDispatch> Dispatched => dispatched;

    public IReadOnlyList<Guid> Nudged => nudged;

    public IReadOnlyList<Guid> Stopped => stopped;

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

        if (ReportedSurface is { } surface && !dispatch.Grant.IsTheSurface(surface))
        {
            // A surface that is not the grant ends the run failed here, before any model call:
            // the agent never reports in (GUARD-001).
            RunUnderWay = false;
            report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
        }

        return Task.CompletedTask;
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

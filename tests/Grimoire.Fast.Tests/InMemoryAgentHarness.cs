using Grimoire.Agent;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The Fast suite's stand-in for the agent. An in-memory adapter at an owned port, which is the
/// only kind of double this project uses (Constitution III.9) — the real adapter is
/// <c>HarnessProcess</c>, and the Contract suite drives that against the real CLI.
/// </summary>
/// <remarks>
/// It reports nothing on its own. A test that wants the agent to report in, or the run to end,
/// says so itself, which is what keeps the run's timing out of the suite's 15 s budget.
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

    /// <summary>Set to make the next dispatch fail, as a process that will not start does.</summary>
    public Exception? DispatchFailure { get; set; }

    public Task DispatchAsync(AgentDispatch dispatch, RunReport report, CancellationToken cancellationToken)
    {
        if (DispatchFailure is { } failure)
        {
            reports[dispatch.SubmissionId] = report;
            return Task.FromException(failure);
        }

        dispatched.Add(dispatch);
        reports[dispatch.SubmissionId] = report;
        RunUnderWay = true;
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

    /// <summary>What the CLI's <c>system/init</c> event does to the run (contracts/agent-cli-protocol.md).</summary>
    public void ReportIn(Guid submissionId) => reports[submissionId].AgentReportedIn(submissionId);

    /// <summary>What the CLI's <c>result</c> event, and the hub's decision on it, do to the run.</summary>
    public void End(Guid submissionId, RunOutcome outcome)
    {
        RunUnderWay = false;
        reports[submissionId].RunEnded(submissionId, outcome);
    }
}

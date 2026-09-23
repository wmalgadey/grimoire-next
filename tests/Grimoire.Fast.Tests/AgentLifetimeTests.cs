using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// No agent goes on working on a run Grimoire has ended: the run under way is stopped as the hub
/// goes down, and one whose agent outlived a stop is terminated as the hub comes back up
/// (RUNS-006).
/// </summary>
/// <remarks>
/// Both halves are decisions of ours and are proven here. That a real process actually dies — and
/// that one carrying a reused identifier is left alone — is an act on the operating system, which
/// no in-memory adapter can make true; that is the Contract suite's, against a real child
/// (research.md R-11).
/// </remarks>
[Trait("level", "fast")]
public sealed class AgentLifetimeTests
{
    private static readonly AgentProcessIdentity TheAgent = new(4242, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    private readonly FastHub before = new();

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task HubStops_StopsTheRunUnderWay()
    {
        var submission = await before.AcceptedAsync("The text being worked when Grimoire stops.");
        before.Harness.ReportIn(submission.Id);
        var run = before.Conductor.Of(submission.Id)!;

        await before.Conductor.StopEverythingAsync();

        // Stopped the way a ceiling stops it — the interrupt, with the kill behind it (DEC-016) —
        // and ended with it, so no agent is left working on a run Grimoire has ended.
        Assert.Equal([run.Id], before.Harness.Stopped);
        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task HubStops_StopsNothing_WithNoRunUnderWay()
    {
        var submission = await before.AcceptedAsync("The text whose run already ended.");
        before.Harness.End(submission.Id, RunOutcome.Done);

        await before.Conductor.StopEverythingAsync();

        Assert.Empty(before.Harness.Stopped);
        Assert.Equal(SubmissionState.Done, submission.State);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_TerminatesTheAgentOfARunThatWasInProgress()
    {
        before.Harness.AgentProcess = TheAgent;
        var submission = await before.AcceptedAsync("The text being worked when Grimoire was killed.");
        before.Harness.ReportIn(submission.Id);

        var after = before.Restarted();

        // Found by the identity recorded with its run, which is the pair — the number and the
        // moment that process started — because the number alone is not an identity.
        Assert.Equal([TheAgent], after.Harness.Terminated);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_TerminatesTheAgent_BeforeTheRunReadsFailedAndBeforeAnythingStarts()
    {
        before.Harness.AgentProcess = TheAgent;
        var interrupted = await before.AcceptedAsync("The text being worked when Grimoire was killed.");
        before.Harness.ReportIn(interrupted.Id);
        before.Clock.Advance(TimeSpan.FromMinutes(1));
        var waiting = await before.AcceptedAsync("The text waiting behind it.");

        var after = before.Restarted();
        await after.AcknowledgeAsync(interrupted.Id);

        // The order is the requirement's, not an implementation detail: the browser must never
        // show failed while the agent is still at work, and no second run may begin beside a first
        // that is still writing (research.md R-11).
        var terminated = after.Journal.When($"terminated {TheAgent.ProcessId}");
        var readsFailed = after.Journal.When($"{interrupted.Id} reads {SubmissionState.Failed}");
        var started = after.Journal.When($"dispatched {waiting.Id}");

        Assert.True(terminated >= 0 && readsFailed >= 0 && started >= 0);
        Assert.True(terminated < readsFailed, "the agent is terminated before its run reads failed");
        Assert.True(readsFailed < started, "nothing starts before that run has read failed");
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_TerminatesNothing_WhenTheRunHadNoAgent()
    {
        // A run dispatched by a harness that never reported a child — and a run that ended before
        // the stop. Neither has an agent to end.
        var dispatched = await before.AcceptedAsync("The text whose run never got a process.");
        before.Clock.Advance(TimeSpan.FromMinutes(1));
        before.Harness.End(dispatched.Id, RunOutcome.Done);

        var after = before.Restarted();

        Assert.Empty(after.Harness.Terminated);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_TerminatesNothing_WhenTheRunHadAlreadyEnded()
    {
        before.Harness.AgentProcess = TheAgent;
        var submission = await before.AcceptedAsync("The text whose run ended before the stop.");
        before.Harness.ReportIn(submission.Id);
        before.Harness.End(submission.Id, RunOutcome.Done);

        var after = before.Restarted();

        // Its agent is long gone, and the number it had may belong to something else by now.
        Assert.Empty(after.Harness.Terminated);
    }
}

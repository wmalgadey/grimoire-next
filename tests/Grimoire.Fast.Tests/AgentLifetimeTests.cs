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

        await before.StopEverythingAsync();

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

        await before.StopEverythingAsync();

        Assert.Empty(before.Harness.Stopped);
        Assert.Equal(SubmissionState.Done, submission.State);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task HubStops_StartsNothingBehindIt_WithASubmissionWaiting()
    {
        var underWay = await before.AcceptedAsync("The text being worked when Grimoire stops.");
        before.Harness.ReportIn(underWay.Id);
        before.Clock.Advance(TimeSpan.FromMinutes(1));
        var waiting = await before.AcceptedAsync("The text waiting behind it.");

        await before.StopEverythingAsync();

        // Stopping a run ends it, and an ending is what lets the next one start — so a hub that
        // stopped without closing the queue first would dispatch this one on its way out, and that
        // agent would be started by a Grimoire already leaving and stopped by nothing.
        Assert.Equal([underWay.Id], before.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(SubmissionState.Submitted, waiting.State);
        Assert.Null(waiting.RunId);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task HubStops_StartsNothingAfterwards_WhenATextArrives()
    {
        await before.StopEverythingAsync();

        // Nothing was under way, so no failure holds the queue (RUNS-003): only the stop itself
        // stands between this text and an agent nothing would ever stop.
        var late = await before.AcceptedAsync("The text that arrives after Grimoire began to stop.");

        Assert.Empty(before.Harness.Dispatched);
        Assert.Null(late.RunId);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_TerminatesNothing_WhenAQuestionsRunHadAlreadyEnded()
    {
        before.Harness.AgentProcess = TheAgent;
        var question = await before.AskedAsync();
        before.Harness.ReportIn(question.Id);
        before.Harness.End(question.Id, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry);

        var after = before.Restarted();

        // A question's run is ended in the store by its run alone, there being no submission to set
        // done. An ending that never reached it would leave the run in progress there, and the
        // start-up would terminate a number that may belong to another process by now.
        Assert.Empty(after.Harness.Terminated);
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
    public async Task Restart_TerminatesTheAgent_BeforeTheRunReadsFailed()
    {
        var (after, interrupted, _) = await RestartedWithASubmissionInterruptedAsync();

        // The order is the requirement's, not an implementation detail: the browser must never
        // show failed while the agent is still at work (research.md R-11).
        var terminated = after.Journal.When($"terminated {TheAgent.ProcessId}");
        var readsFailed = after.Journal.When($"{interrupted.Id} reads {SubmissionState.Failed}");

        Assert.True(terminated >= 0 && readsFailed >= 0);
        Assert.True(terminated < readsFailed, "the agent is terminated before its run reads failed");
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_StartsNothing_BeforeTheInterruptedRunReadsFailed()
    {
        var (after, interrupted, waiting) = await RestartedWithASubmissionInterruptedAsync();

        // No second run may begin beside a first that is still writing (research.md R-11).
        var readsFailed = after.Journal.When($"{interrupted.Id} reads {SubmissionState.Failed}");
        var started = after.Journal.When($"dispatched {waiting.Id}");

        Assert.True(readsFailed >= 0 && started >= 0);
        Assert.True(readsFailed < started, "nothing starts before that run has read failed");
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_TerminatesTheAgentOfAQuestion_BeforeItsRunReadsFailed()
    {
        var (after, runId, _) = await RestartedWithAQuestionInterruptedAsync();

        // A question's run has no submission to read failed off; it reads failed by being marked
        // ended in the store. The agent still holding the granted tools is gone before its run is
        // written off, as a submission's is.
        var terminated = after.Journal.When($"terminated {TheAgent.ProcessId}");
        var readsFailed = after.Journal.When($"run {runId} ended");

        Assert.True(terminated >= 0 && readsFailed >= 0);
        Assert.True(terminated < readsFailed, "the agent is terminated before its run reads failed");
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Restart_StartsNothing_BeforeAnInterruptedQuestionsRunReadsFailed()
    {
        var (after, runId, waiting) = await RestartedWithAQuestionInterruptedAsync();

        // The submission starts with no acknowledgement, because the question's failure went with
        // the chat (RUNS-003, QUERY-005) — but not before the question's run is written off.
        var readsFailed = after.Journal.When($"run {runId} ended");
        var started = after.Journal.When($"dispatched {waiting.Id}");

        Assert.True(readsFailed >= 0 && started >= 0);
        Assert.True(readsFailed < started, "nothing starts before that run has read failed");
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task SecondRestart_TerminatesNothing_AfterAQuestionsAgentWasTerminated()
    {
        before.Harness.AgentProcess = TheAgent;
        var question = await before.AskedAsync();
        before.Harness.ReportIn(question.Id);
        var runId = question.RunId!.Value;

        var first = before.Restarted();
        var second = first.Restarted();

        // The first start-up wrote the run off, so the second finds nothing of it in progress: the
        // number it once had may belong to another process by now, and a run ended once is not
        // dispatched, ended or terminated again.
        Assert.Empty(second.Harness.Terminated);
        Assert.Empty(second.Harness.Dispatched);
        Assert.Single(second.Journal.Entries, e => e == $"run {runId} ended");
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

    private async Task<(FastHub After, Submission Interrupted, Submission Waiting)> RestartedWithASubmissionInterruptedAsync()
    {
        before.Harness.AgentProcess = TheAgent;
        var interrupted = await before.AcceptedAsync("The text being worked when Grimoire was killed.");
        before.Harness.ReportIn(interrupted.Id);
        before.Clock.Advance(TimeSpan.FromMinutes(1));
        var waiting = await before.AcceptedAsync("The text waiting behind it.");

        var after = before.Restarted();
        await after.AcknowledgeAsync(interrupted.Id);
        return (after, interrupted, waiting);
    }

    private async Task<(FastHub After, Guid RunId, Submission Waiting)> RestartedWithAQuestionInterruptedAsync()
    {
        before.Harness.AgentProcess = TheAgent;
        var question = await before.AskedAsync();
        before.Harness.ReportIn(question.Id);
        var runId = question.RunId!.Value;
        before.Clock.Advance(TimeSpan.FromMinutes(1));
        var waiting = await before.AcceptedAsync("The text waiting behind the question.");

        return (before.Restarted(), runId, waiting);
    }
}

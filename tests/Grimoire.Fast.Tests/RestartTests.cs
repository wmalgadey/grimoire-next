using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// Grimoire stopped and started again: every submission is still there with the state it carried,
/// and a run that was in progress reads failed (RUNS-004).
/// </summary>
/// <remarks>
/// The rule that turns what the store held into states is the board's, and that is what is proven
/// here. That the file itself keeps them is the Contract suite's, against a real SQLite file; the
/// two together are what a restart is (research.md R-09).
/// </remarks>
[Trait("level", "fast")]
public sealed class RestartTests
{
    private readonly FastHub before = new();

    private async Task<Submission> SubmittedAsync(string text)
    {
        var accepted = await before.AcceptedAsync(text);
        before.Clock.Advance(TimeSpan.FromMinutes(1));
        return accepted;
    }

    private static Submission In(FastHub hub, Guid id) => hub.Board.Find(id)!;

    [Fact]
    [Trait("req", "RUNS-004")]
    public async Task Restart_ListsEverySubmission_WithTheStateItCarried()
    {
        var done = await SubmittedAsync("The text whose run finished.");
        before.Harness.End(done.Id, RunOutcome.Done);

        var failed = await SubmittedAsync("The text whose run failed.");
        before.Harness.End(failed.Id, RunOutcome.Failed);
        await before.AcknowledgeAsync(failed.Id);

        var waiting = await SubmittedAsync("The text that never got its turn.");
        before.Harness.ReportIn(waiting.Id);

        var after = before.Restarted();

        Assert.Equal(SubmissionState.Done, In(after, done.Id).State);
        Assert.Equal(SubmissionState.Failed, In(after, failed.Id).State);
        Assert.Equal("The text whose run finished.", In(after, done.Id).Text);
        Assert.Equal(done.SubmittedAt, In(after, done.Id).SubmittedAt);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public async Task Restart_LeavesTheRunThatWasInProgressFailed()
    {
        var running = await SubmittedAsync("The text being worked when Grimoire stopped.");
        before.Harness.ReportIn(running.Id);

        var after = before.Restarted();

        // Nothing is resumed and nothing is retried; whatever it wrote stays in the wiki.
        Assert.Equal(SubmissionState.Failed, In(after, running.Id).State);
        Assert.Empty(after.Harness.Dispatched);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public async Task Restart_LeavesTheRunThatWasInProgressFailed_BeforeTheAgentReportedIn()
    {
        // Dispatched, and the agent had not reported in yet: it reads submitted and carries a run,
        // which is what tells it apart from one still waiting its turn (research.md R-04).
        var dispatched = await SubmittedAsync("The text whose agent never reported in.");

        var after = before.Restarted();

        Assert.Equal(SubmissionState.Failed, In(after, dispatched.Id).State);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-002")]
    [Trait("req", "RUNS-003")]
    public async Task Restart_LeavesTheWaitingSubmissionsWaiting_UntilTheFailureIsAcknowledged()
    {
        var interrupted = await SubmittedAsync("The text being worked when Grimoire stopped.");
        before.Harness.ReportIn(interrupted.Id);
        var first = await SubmittedAsync("The text waiting behind it.");
        var second = await SubmittedAsync("The text waiting behind that.");

        var after = before.Restarted();

        // The interrupted run reads failed and holds the queue like any other failure (RUNS-003).
        Assert.Equal(SubmissionState.Submitted, In(after, first.Id).State);
        Assert.Empty(after.Harness.Dispatched);

        await after.AcknowledgeAsync(interrupted.Id);

        // And then they start, in the order they were made (RUNS-002).
        Assert.Equal([first.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));

        after.Harness.End(first.Id, RunOutcome.Done);
        Assert.Equal([first.Id, second.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-003")]
    public async Task Restart_BlocksNothing_WhenTheFailureWasAcknowledgedBeforeTheStop()
    {
        var failed = await SubmittedAsync("The text whose run failed.");
        before.Harness.End(failed.Id, RunOutcome.Failed);
        await before.AcknowledgeAsync(failed.Id);
        var waiting = await SubmittedAsync("The text submitted after it.");
        before.Harness.End(waiting.Id, RunOutcome.Done);

        var after = before.Restarted();
        var later = await after.AcceptedAsync("A text submitted after the restart.");

        // The acknowledgement survived with everything else: a restart does not re-block a queue
        // the user has already cleared, and the run it cleared still reads failed.
        Assert.Equal([later.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(SubmissionState.Failed, In(after, failed.Id).State);
        Assert.False(In(after, failed.Id).AwaitingAcknowledgement);
    }
}

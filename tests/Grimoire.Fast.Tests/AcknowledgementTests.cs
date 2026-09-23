using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a failure does to the queue: it stops there until the user acknowledges it, and the run
/// they acknowledged still reads failed (RUNS-003).
/// </summary>
/// <remarks>
/// The acknowledgement names a submission, not a run. A submission has exactly one run here
/// (INGEST-002), so its identifier already identifies the failed one — and it is the identifier
/// the browser has carried as its row key since `001-first-ingest` (research.md R-06).
/// </remarks>
[Trait("level", "fast")]
public sealed class AcknowledgementTests
{
    private readonly FastHub hub = new();

    private async Task<Submission> SubmittedAsync(string text)
    {
        var accepted = await hub.AcceptedAsync(text);
        hub.Clock.Advance(TimeSpan.FromMinutes(1));
        return accepted;
    }

    /// <summary>A failed run with two submissions waiting behind it.</summary>
    private async Task<(Submission Failed, Submission Next, Submission Last)> AFailureWithTwoWaitingAsync()
    {
        var failed = await SubmittedAsync("The text whose run fails.");
        var next = await SubmittedAsync("The text behind it.");
        var last = await SubmittedAsync("The text behind that.");

        hub.Harness.End(failed.Id, RunOutcome.Failed);

        return (failed, next, last);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task RunFails_StartsNothingFurther_WithSubmissionsWaiting()
    {
        var (failed, next, last) = await AFailureWithTwoWaitingAsync();

        Assert.Equal(SubmissionState.Failed, failed.State);
        Assert.Equal(SubmissionState.Submitted, next.State);
        Assert.Equal(SubmissionState.Submitted, last.State);

        // Only the failed run was ever dispatched: the queue stops at a failure.
        Assert.Equal([failed.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Submit_StartsNoRun_WhileAFailureIsUnacknowledged()
    {
        var (failed, _, _) = await AFailureWithTwoWaitingAsync();

        var later = await SubmittedAsync("A text submitted after the failure.");

        // Accepted, and waiting like the others: what blocks is the failure, not acceptance.
        Assert.Equal(SubmissionState.Submitted, later.State);
        Assert.Equal([failed.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_StartsTheNextWaitingSubmission()
    {
        var (failed, next, last) = await AFailureWithTwoWaitingAsync();

        await hub.AcknowledgeAsync(failed.Id);

        // The next one, and only the next one: the one behind it waits its own turn.
        Assert.Equal([failed.Id, next.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(SubmissionState.Submitted, last.State);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_LeavesTheRunFailed()
    {
        var (failed, _, _) = await AFailureWithTwoWaitingAsync();

        await hub.AcknowledgeAsync(failed.Id);

        // Acknowledging is not a state and changes none: it says the user has seen the failure.
        Assert.Equal(SubmissionState.Failed, failed.State);
        Assert.False(failed.AwaitingAcknowledgement);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_StartsNothing_WithNothingWaiting()
    {
        var only = await SubmittedAsync("The only text.");
        hub.Harness.End(only.Id, RunOutcome.Failed);

        await hub.AcknowledgeAsync(only.Id);

        Assert.Single(hub.Harness.Dispatched);

        // And the next text the user submits starts as usual.
        var later = await SubmittedAsync("A text submitted afterwards.");
        Assert.Equal([only.Id, later.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_ChangesNothing_WhenItWasAcknowledgedAlready()
    {
        var (failed, next, last) = await AFailureWithTwoWaitingAsync();
        await hub.AcknowledgeAsync(failed.Id);
        hub.Harness.ReportIn(next.Id);

        // A page loaded before the last run failed sends this: it names a submission that is no
        // longer an unacknowledged failure.
        await hub.AcknowledgeAsync(failed.Id);

        Assert.Equal([failed.Id, next.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(SubmissionState.Running, next.State);
        Assert.Equal(SubmissionState.Submitted, last.State);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_ChangesNothing_WhenTheSubmissionHasNotFailed()
    {
        var (failed, next, _) = await AFailureWithTwoWaitingAsync();

        await hub.AcknowledgeAsync(next.Id);

        // A submission that is waiting is not a failure, so nothing is cleared and nothing starts.
        Assert.Equal([failed.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.True(failed.AwaitingAcknowledgement);
        Assert.Equal(SubmissionState.Submitted, next.State);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_ChangesNothing_WhenTheSubmissionIsUnknown()
    {
        var (failed, _, _) = await AFailureWithTwoWaitingAsync();

        await hub.AcknowledgeAsync(Guid.NewGuid());

        Assert.Equal([failed.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.True(failed.AwaitingAcknowledgement);
    }
}

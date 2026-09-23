using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The queue rule: a text handed over while a run is under way waits its turn, and waiting
/// submissions start one at a time in the order they were made (RUNS-002).
/// </summary>
/// <remarks>
/// The board decides both halves — whether a run may start and which submission is next — and it
/// decides them under the one lock it already holds. What dispatches is the hub's
/// <c>RunQueue</c>, which asks and acts and judges nothing (research.md R-03).
/// </remarks>
[Trait("level", "fast")]
public sealed class QueueTests
{
    private readonly FastHub hub = new();

    /// <summary>
    /// Submissions made a minute apart, so that the order they start in is the order they were
    /// <em>made</em> rather than the order they happen to sit in a list.
    /// </summary>
    private async Task<Submission> SubmittedAsync(string text)
    {
        var accepted = await hub.AcceptedAsync(text);
        hub.Clock.Advance(TimeSpan.FromMinutes(1));
        return accepted;
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task Submit_IsAccepted_WhileARunIsUnderWay()
    {
        var first = await SubmittedAsync("The first text.");
        hub.Harness.ReportIn(first.Id);

        var second = await SubmittedAsync("The second text.");

        // Accepted rather than refused: refusing was INGEST-005, retired with this feature.
        Assert.Equal(SubmissionState.Submitted, second.State);
        Assert.Equal([second, first], hub.Board.All);
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task Submit_StartsNoSecondRun_WhileARunIsUnderWay()
    {
        var first = await SubmittedAsync("The first text.");
        hub.Harness.ReportIn(first.Id);

        await SubmittedAsync("The second text.");
        await SubmittedAsync("The third text.");

        // The second and third wait. One run is under way, which is all that ever is.
        Assert.Equal([first.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task RunEnds_StartsTheWaitingSubmission()
    {
        var first = await SubmittedAsync("The first text.");
        var second = await SubmittedAsync("The second text.");

        hub.Harness.End(first.Id, RunOutcome.Done);

        // Nobody asked for it: the run that ended is what let the next one start.
        Assert.Equal([first.Id, second.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(SubmissionState.Submitted, second.State);
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task RunsStart_InTheOrderTheSubmissionsWereMade()
    {
        var first = await SubmittedAsync("The first text.");
        var second = await SubmittedAsync("The second text.");
        var third = await SubmittedAsync("The third text.");

        hub.Harness.End(first.Id, RunOutcome.Done);
        hub.Harness.End(second.Id, RunOutcome.Done);

        Assert.Equal(
            [first.Id, second.Id, third.Id],
            hub.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task RunEnds_StartsExactlyOneRun_WithSeveralSubmissionsWaiting()
    {
        var first = await SubmittedAsync("The first text.");
        var second = await SubmittedAsync("The second text.");
        await SubmittedAsync("The third text.");

        hub.Harness.End(first.Id, RunOutcome.Done);

        // A submission handed out once is never handed out again, and the one behind it stays
        // waiting: at most one run is in progress at any time.
        Assert.Equal([first.Id, second.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.True(hub.Harness.RunUnderWay);
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task RunEnds_StartsTheNextSubmission_AfterAFailedRun()
    {
        var first = await SubmittedAsync("The first text.");
        var second = await SubmittedAsync("The second text.");

        hub.Harness.End(first.Id, RunOutcome.Failed);

        // What a failure does to the queue is RUNS-003's, and it is not built yet: here a failed
        // run is simply a run that has ended, and the next one starts.
        Assert.Equal([first.Id, second.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task RunsStart_InTheOrderTheSubmissionsWereMade_AfterTheClockWasPutBack()
    {
        var first = await SubmittedAsync("The first text.");

        // The machine's clock is corrected backwards between the two — an NTP step, or the owner
        // changing the time — so the second submission carries the earlier stamp of the two. The
        // order they were made in has not changed, and neither may the order they run in.
        hub.Clock.AdjustTime(FastSuite.Start.AddMinutes(-10));
        var second = await SubmittedAsync("The second text.");

        Assert.True(second.SubmittedAt < first.SubmittedAt);

        hub.Harness.End(first.Id, RunOutcome.Done);

        Assert.Equal([first.Id, second.Id], hub.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    public async Task RunEnds_StartsNothing_WithNothingWaiting()
    {
        var only = await SubmittedAsync("The only text.");

        hub.Harness.End(only.Id, RunOutcome.Done);

        Assert.Single(hub.Harness.Dispatched);
        Assert.False(hub.Harness.RunUnderWay);
    }
}

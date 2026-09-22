using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The two fixed ceilings a run has, and what cost means (GUARD-004).
/// </summary>
/// <remarks>
/// Driven by <c>FakeTimeProvider</c>: the elapsed-time ceiling is the one requirement that would
/// otherwise make a test wait for real seconds, and the Fast suite has 15 s for the whole of it
/// (Constitution III.7, research.md R-12).
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "GUARD-004")]
public sealed class CeilingTests
{
    private static ModelTokens Tokens(long each) => new(each, each, each, each);

    [Fact]
    public void Ceilings_AreFixedValuesAndNotSettings()
    {
        // docs/product.md §4 rules out configurable budgets and per-run tuning outright.
        Assert.Equal(TimeSpan.FromMinutes(15), Ceilings.Fixed.Elapsed);
        Assert.Equal(2_000_000, Ceilings.Fixed.Tokens);
    }

    [Fact]
    public void Run_ReachesACeiling_WhenTheClockRunsOut()
    {
        var clock = FastSuite.Clock();
        var started = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromMinutes(15));

        Assert.True(Ceilings.Fixed.ReachedBy(clock.GetUtcNow() - started, tokens: 0));
    }

    [Fact]
    public void Run_ReachesACeiling_WhenTheTokensRunOut() =>
        Assert.True(Ceilings.Fixed.ReachedBy(TimeSpan.Zero, Ceilings.Fixed.Tokens));

    [Fact]
    public void Run_ReachesNoCeiling_WhileBothAreClear()
    {
        var clock = FastSuite.Clock();
        var started = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromMinutes(14));

        Assert.False(Ceilings.Fixed.ReachedBy(clock.GetUtcNow() - started, Ceilings.Fixed.Tokens - 1));
    }

    [Fact]
    public void Cost_CountsTheFourTokenFields() =>
        Assert.Equal(4, Ceilings.CostOf([Tokens(1)]));

    [Fact]
    public void Cost_CountsEveryModelTheRunTouched()
    {
        // A modelUsage with more than one model in it: the CLI spends tokens on background calls
        // of its own, and a call the run never asked for is still the run's doing (R-04).
        var asked = Tokens(100);
        var background = Tokens(3);

        Assert.Equal(412, Ceilings.CostOf([asked, background]));
    }

    [Fact]
    public void Cost_CountsNothing_WithoutAModelUsage() =>
        Assert.Equal(0, Ceilings.CostOf([]));

    [Fact]
    public async Task Run_IsStoppedThroughThePort_WhenTheCostCeilingIsReached()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Tokens);

        // At once, a model call in flight included: the stop goes out rather than the hub waiting
        // for the turn to finish on its own (research.md R-04).
        Assert.Equal([run.Id], hub.Harness.Stopped);
    }

    [Fact]
    public async Task Run_IsStoppedThroughThePort_WhenTheElapsedCeilingIsReached()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);
        hub.Harness.Spend(submission.Id, tokensUsed: 1);

        Assert.Equal([run.Id], hub.Harness.Stopped);
    }

    [Fact]
    public async Task Run_RecordsWhatItSpends_AsTheCostArrives()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();

        hub.Harness.Spend(submission.Id, 1_000);
        hub.Harness.Spend(submission.Id, 7_500);

        // Recorded on the run, not only compared against the ceiling: the decision taken when the
        // agent stops reads this, and a run that had spent nothing would never reach the ceiling.
        Assert.Equal(7_500, hub.Conductor.Of(submission.Id)!.TokensUsed);
    }

    [Fact]
    public async Task Run_EndsFailed_WhenTheTokensItSpentReachTheCeiling()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Tokens);
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Fact]
    public async Task Run_IsLeftAlone_WhileBothCeilingsAreClear()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();

        hub.Clock.Advance(TimeSpan.FromMinutes(14));
        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Tokens - 1);

        Assert.Empty(hub.Harness.Stopped);
    }

    [Fact]
    public async Task Run_IsStoppedByTheClockAlone_WhenTheAgentSaysNothing()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;
        hub.Harness.ReportIn(submission.Id);

        // Not one line from the agent after it reported in: no cost, no stop, nothing to read a
        // clock against. This is the run the elapsed ceiling exists for — a hung model call or a
        // tool call that never comes back — and it is the clock that has to raise it.
        hub.Clock.Advance(Ceilings.Fixed.Elapsed);

        Assert.Equal([run.Id], hub.Harness.Stopped);
    }

    [Fact]
    public async Task Run_EndsFailed_WhenTheClockRunsOutAndTheAgentNeverReportsAgain()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);

        // A stopped run that never says how it ended still ends. Left running, the board would
        // refuse every later submission as a run in progress for as long as the process lives.
        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Fact]
    public async Task Run_EndsFailed_WhenTheCostCeilingIsReachedAndTheAgentNeverReportsAgain()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        // The interrupt goes out and the agent answers nothing — a turn that was already wedged
        // when its streamed usage crossed the ceiling. The cost ceiling has to end the run for
        // the same reason the elapsed one does, or the board refuses every later text.
        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Tokens);

        Assert.Equal(SubmissionState.Failed, submission.State);
        Assert.Null(hub.Conductor.Of(submission.Id));
    }

    [Fact]
    public async Task Run_IsNotStoppedByTheClock_BeforeTheCeiling()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed - TimeSpan.FromSeconds(1));

        Assert.Empty(hub.Harness.Stopped);
        Assert.Equal(SubmissionState.Running, submission.State);
    }

    [Fact]
    public async Task Run_IsNotStoppedByTheClock_AfterItHasAlreadyEnded()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);
        await hub.Wiki.AppendLogAsync(
            $"Run {hub.Conductor.Of(submission.Id)!.Id} added one page.\n", TestContext.Current.CancellationToken);
        await hub.Harness.StoppedAsync(submission.Id);

        // The run is over and its timer went with it; the clock moving on is not a second ending.
        hub.Clock.Advance(Ceilings.Fixed.Elapsed * 2);

        Assert.Empty(hub.Harness.Stopped);
        Assert.Equal(SubmissionState.Done, submission.State);
    }

    [Fact]
    public async Task Run_EndsFailed_AfterACeilingStopsIt()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);
        hub.Harness.Spend(submission.Id, tokensUsed: 1);
        await hub.Harness.StoppedAsync(submission.Id, endedAbnormally: true);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }
}

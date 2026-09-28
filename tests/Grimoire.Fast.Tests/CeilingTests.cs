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
public sealed class CeilingTests
{
    private static ModelTokens Tokens(long each) => new(each, each, each, each);

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Ceilings_AreFixedValuesAndNotSettings()
    {
        // docs/product.md §4 rules out configurable budgets and per-run tuning outright.
        Assert.Equal(TimeSpan.FromMinutes(15), Ceilings.Fixed.Elapsed);
        Assert.Equal(2_000_000, Ceilings.Fixed.Cost);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Run_ReachesACeiling_WhenTheClockRunsOut()
    {
        var clock = FastSuite.Clock();
        var started = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromMinutes(15));

        Assert.True(Ceilings.Fixed.ReachedBy(clock.GetUtcNow() - started, cost: 0));
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Run_ReachesACeiling_WhenTheCostRunsOut() =>
        Assert.True(Ceilings.Fixed.ReachedBy(TimeSpan.Zero, Ceilings.Fixed.Cost));

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Run_ReachesNoCeiling_WhileBothAreClear()
    {
        var clock = FastSuite.Clock();
        var started = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromMinutes(14));

        Assert.False(Ceilings.Fixed.ReachedBy(clock.GetUtcNow() - started, Ceilings.Fixed.Cost - 1));
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Cost_WeighsEachTokenClassByWhatItIsBilledAt()
    {
        // One token of each class, weighed one class at a time: an input token is 1, an output
        // token 5, a cache write 2, and ten cache reads are 1. The four ratios are Anthropic's
        // price structure, and the sign-in contract test is what holds them to the CLI's own
        // costUSD (DEC-015).
        Assert.Equal(1, Ceilings.CostOf(new ModelTokens(1, 0, 0, 0)));
        Assert.Equal(5, Ceilings.CostOf(new ModelTokens(0, 1, 0, 0)));
        Assert.Equal(1, Ceilings.CostOf(new ModelTokens(0, 0, 10, 0)));
        Assert.Equal(2, Ceilings.CostOf(new ModelTokens(0, 0, 0, 1)));
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Cost_CountsEveryModelTheRunTouched()
    {
        // A modelUsage with more than one model in it: the CLI spends tokens on background calls
        // of its own, and a call the run never asked for is still the run's doing (R-04).
        var asked = Tokens(100);
        var background = Tokens(3);

        // 103 of each class: 103 + 515 + 10 + 206, the cache reads rounded down together with the
        // rest rather than on their own.
        Assert.Equal(834, Ceilings.CostOf([asked, background]));
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Cost_CountsNothing_WithoutAModelUsage() =>
        Assert.Equal(0, Ceilings.CostOf([]));

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Run_ReachesNoCeiling_WithTenMillionCacheReads()
    {
        // Five times the ceiling in raw tokens, and half of it in cost. A run that reads a large
        // cache back turn after turn is the cheapest thing the CLI does, and the raw sum used to
        // stop it — which measured turns × context size and not what the run cost (GUARD-004).
        // The output run below costs twice this and the raw sum let it through.
        var reads = new ModelTokens(0, 0, 10_000_000, 0);

        Assert.Equal(1_000_000, Ceilings.CostOf(reads));
        Assert.False(Ceilings.Fixed.ReachedBy(TimeSpan.Zero, Ceilings.CostOf(reads)));
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public void Run_ReachesACeiling_WithFourHundredThousandOutputTokens()
    {
        // A fifth of the ceiling in raw tokens, and the whole of it in cost. Output is the dearest
        // of the four classes, so the raw sum let this run five times as far as it should — while
        // stopping the ten million cache reads above, which cost half as much.
        var written = new ModelTokens(0, 400_000, 0, 0);

        Assert.Equal(2_000_000, Ceilings.CostOf(written));
        Assert.True(Ceilings.Fixed.ReachedBy(TimeSpan.Zero, Ceilings.CostOf(written)));
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task Run_IsStoppedThroughThePort_WhenTheCostCeilingIsReached()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Cost);

        // At once, a model call in flight included: the stop goes out rather than the hub waiting
        // for the turn to finish on its own (research.md R-04).
        Assert.Equal([run.Id], hub.Harness.Stopped);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task Run_IsStoppedThroughThePort_WhenTheElapsedCeilingIsReached()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);
        hub.Harness.Spend(submission.Id, costSpent: 1);

        Assert.Equal([run.Id], hub.Harness.Stopped);
    }

    [Fact]
    [Trait("req", "RUNS-010")]
    public async Task Run_RecordsWhatItSpends_AsTheCostArrives()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();

        hub.Harness.Spend(submission.Id, 1_000);
        hub.Harness.Spend(submission.Id, 7_500);

        // Recorded on the run, not only compared against the ceiling: the decision taken when the
        // agent stops reads this, and a run that had spent nothing would never reach the ceiling.
        Assert.Equal(7_500, hub.Conductor.Of(submission.Id)!.CostSpent);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task Run_EndsFailed_WhenWhatItSpentReachesTheCostCeiling()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Cost);
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task Run_IsLeftAlone_WhileBothCeilingsAreClear()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();

        hub.Clock.Advance(TimeSpan.FromMinutes(14));
        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Cost - 1);

        Assert.Empty(hub.Harness.Stopped);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
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
    [Trait("req", "GUARD-004")]
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
    [Trait("req", "GUARD-004")]
    public async Task Run_EndsFailed_WhenTheCostCeilingIsReachedAndTheAgentNeverReportsAgain()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        // The interrupt goes out and the agent answers nothing — a turn that was already wedged
        // when its streamed usage crossed the ceiling. The cost ceiling has to end the run for
        // the same reason the elapsed one does, or the board refuses every later text.
        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Cost);

        Assert.Equal(SubmissionState.Failed, submission.State);
        Assert.Null(hub.Conductor.Of(submission.Id));
    }

    [Fact]
    [Trait("req", "GUARD-004")]
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
    [Trait("req", "GUARD-004")]
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
    [Trait("req", "GUARD-004")]
    public async Task Run_EndsFailed_AfterACeilingStopsIt()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);
        hub.Harness.Spend(submission.Id, costSpent: 1);
        await hub.Harness.StoppedAsync(submission.Id, endedAbnormally: true);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }
}

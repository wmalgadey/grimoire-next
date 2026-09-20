using Grimoire.Agent;

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
}

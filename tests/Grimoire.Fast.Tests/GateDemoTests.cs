namespace Grimoire.Fast.Tests;

/// <summary>
/// Throwaway. It exists to make both gates fail once on a real violation (Constitution II.2,
/// task T006) and is removed again with the branch it lives on.
/// </summary>
public sealed class GateDemoTests
{
    /// <summary>INGEST-999 is registered nowhere: rule 2 of Constitution IV.3.</summary>
    [Fact]
    [Trait("level", "fast")]
    [Trait("req", "INGEST-999")]
    public void TraceCheckRefusesAnUnknownRequirementId() => Assert.True(true);

    /// <summary>Twenty seconds against a 15 s budget: the Fast suite's time-budget.</summary>
    [Fact]
    [Trait("level", "fast")]
    public async Task TimeBudgetRefusesAFastSuitePastFifteenSeconds() =>
        await Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
}

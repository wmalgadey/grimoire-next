namespace Grimoire.Agent;

/// <summary>What one model was given and produced, as the CLI's <c>modelUsage</c> reports it.</summary>
public readonly record struct ModelTokens(
    long InputTokens,
    long OutputTokens,
    long CacheReadInputTokens,
    long CacheCreationInputTokens)
{
    public long Total => InputTokens + OutputTokens + CacheReadInputTokens + CacheCreationInputTokens;
}

/// <summary>
/// The two fixed ceilings on a run: elapsed time, and cost counted in model tokens (GUARD-004).
/// </summary>
/// <remarks>
/// <para>
/// Fixed values, not settings. <c>docs/product.md</c> §4 rules out configurable budgets and per-run
/// tuning outright: trust rests on seeing what a run did, not on regulating it up front. The owner
/// revises these after the acceptance run by changing them here.
/// </para>
/// <para>
/// One mechanism serves both, because inside a single turn the agent loops model call → tool call →
/// model call by itself and nothing can prevent the next call without ending the one in flight. At
/// either ceiling the hub sends the same interrupt and the run ends failed (research.md R-04).
/// </para>
/// </remarks>
public sealed record Ceilings(TimeSpan Elapsed, long Tokens)
{
    /// <summary>2 000 000 tokens and 15 minutes, as the plan sets them.</summary>
    public static readonly Ceilings Fixed = new(TimeSpan.FromMinutes(15), 2_000_000);

    public bool ReachedBy(TimeSpan elapsed, long tokens) => elapsed >= Elapsed || tokens >= Tokens;

    /// <summary>
    /// Every token the run caused: the four fields of <b>every</b> entry of the run's
    /// <c>modelUsage</c>, all models and the CLI's own background calls included. A background
    /// call the run never asked for is still the run's doing (research.md R-04).
    /// </summary>
    public static long CostOf(IEnumerable<ModelTokens> modelUsage) => modelUsage.Sum(m => m.Total);
}

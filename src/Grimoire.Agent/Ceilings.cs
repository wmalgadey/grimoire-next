namespace Grimoire.Agent;

/// <summary>What one model was given and produced, as the CLI's <c>modelUsage</c> reports it.</summary>
/// <remarks>
/// The four classes are kept apart and never summed into one number, because they do not cost the
/// same: <see cref="Ceilings.CostOf(ModelTokens)"/> is what turns them into the one quantity the
/// cost ceiling counts. The raw four travel with the run and are written down per run, which is what
/// the ceiling's calibration will be read off (GUARD-004, RUNS-008).
/// </remarks>
public readonly record struct ModelTokens(
    long InputTokens,
    long OutputTokens,
    long CacheReadInputTokens,
    long CacheCreationInputTokens)
{
    /// <summary>The four classes of two counts, added class by class.</summary>
    public static ModelTokens operator +(ModelTokens left, ModelTokens right) => left.Plus(right);

    /// <summary>The named form of <c>+</c>, which CA2225 asks for.</summary>
    public ModelTokens Plus(ModelTokens other) => new(
        InputTokens + other.InputTokens,
        OutputTokens + other.OutputTokens,
        CacheReadInputTokens + other.CacheReadInputTokens,
        CacheCreationInputTokens + other.CacheCreationInputTokens);
}

/// <summary>
/// The two fixed ceilings on a run: elapsed time, and cost counted in input-token equivalents
/// (GUARD-004).
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
public sealed record Ceilings(TimeSpan Elapsed, long Cost)
{
    // The four weights, in tenths of an input token: an input token is 1, an output token 5, a
    // cache read a tenth, and a cache write 2. They are Anthropic's price *structure* and not its
    // prices — every first-party model is billed at those ratios against its own input price, so a
    // quantity weighted by them is proportional to what a run costs whatever model it ran on. No
    // absolute price is in this tree; the one place a currency figure appears is the sign-in
    // contract test that verifies these four against the CLI's own `costUSD` (DEC-015).
    //
    // The cache write is weighted as a *1-hour* write, which is what the spike observed the CLI
    // asking for (`cache_creation.ephemeral_1h_input_tokens`) and is the dearer of the two.
    // Uniformly conservative: a 5-minute write costs 1.25 and is counted as 2, so the ceiling is
    // never reached later than the money says.
    //
    // Not configurable, for the reason the ceilings themselves are not.
    private const long InputWeight = 10;
    private const long OutputWeight = 50;
    private const long CacheReadWeight = 1;
    private const long CacheWriteWeight = 20;

    /// <summary>The scale the weights are written in, divided out once at the end.</summary>
    private const long Tenths = 10;

    /// <summary>
    /// 2 000 000 equivalents and 15 minutes.
    /// </summary>
    /// <remarks>
    /// <b>The cost figure is a placeholder.</b> It is the token count the old raw sum was held to,
    /// carried over unchanged, and an equivalent is not a token: the same run now counts differently,
    /// and how much differently is what the acceptance run measures. The raw counters written down
    /// per run are what it is calibrated from (GUARD-004).
    /// </remarks>
    public static readonly Ceilings Fixed = new(TimeSpan.FromMinutes(15), 2_000_000);

    public bool ReachedBy(TimeSpan elapsed, long cost) => elapsed >= Elapsed || cost >= Cost;

    /// <summary>
    /// What one model's four counts cost, in input-token equivalents.
    /// </summary>
    /// <remarks>
    /// Weighted in tenths and divided once, so the arithmetic is integer throughout and the same
    /// counters always give the same figure — which is what lets the live sum off the stream and the
    /// reconciliation off <c>modelUsage</c> be compared at all (GUARD-004).
    /// </remarks>
    public static long CostOf(ModelTokens tokens) =>
        ((InputWeight * tokens.InputTokens)
            + (OutputWeight * tokens.OutputTokens)
            + (CacheReadWeight * tokens.CacheReadInputTokens)
            + (CacheWriteWeight * tokens.CacheCreationInputTokens))
        / Tenths;

    /// <summary>
    /// What the run has cost: the four fields of <b>every</b> entry of the run's <c>modelUsage</c>,
    /// all models and the CLI's own background calls included, added class by class and then
    /// weighted. A background call the run never asked for is still the run's doing (research.md
    /// R-04).
    /// </summary>
    /// <remarks>
    /// Added before it is weighted, and not weighted per model and added: one division instead of
    /// one per model, so a breakdown and its total cannot differ by the rounding.
    /// </remarks>
    public static long CostOf(IEnumerable<ModelTokens> modelUsage) =>
        CostOf(Sum(modelUsage));

    /// <summary>The four classes of several models' counts, added class by class.</summary>
    public static ModelTokens Sum(IEnumerable<ModelTokens> modelUsage)
    {
        ArgumentNullException.ThrowIfNull(modelUsage);

        return modelUsage.Aggregate(default(ModelTokens), (running, model) => running + model);
    }
}

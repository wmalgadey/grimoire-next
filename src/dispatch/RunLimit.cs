namespace Grimoire.Dispatch;

/// <summary>
/// The two ceilings a run is held to (FR-009). Configuration, not specification: an operator who
/// finds the limit too tight raises it, and the failure reason is written so they can tell.
/// </summary>
/// <param name="MaxToolCalls">
/// The tool-call ceiling. Counts every call the agent made, refusals included — an agent stuck in
/// a loop of denied attempts is exactly as stuck as one in a loop of successful ones.
/// </param>
/// <param name="MaxElapsedMs">The elapsed-time ceiling, measured from the moment the run starts.</param>
public sealed record RunLimit(int MaxToolCalls, int MaxElapsedMs)
{
    /// <summary>Whether this many tool calls is past the ceiling.</summary>
    public bool ToolCallsExceeded(int toolCallCount) => toolCallCount > MaxToolCalls;

    /// <summary>
    /// The reason recorded when a run is stopped for making too many calls. Names the ceiling, so
    /// "is the limit too tight?" is answerable from the task view alone (plan IV).
    /// </summary>
    public string ToolCallReason(int toolCallCount) =>
        $"The run made {toolCallCount} tool calls, past its ceiling of {MaxToolCalls}. "
        + "Raise GRIMOIRE_RUN_MAX_TOOL_CALLS if this source needs more.";

    /// <summary>The reason recorded when a run is stopped for taking too long.</summary>
    public string ElapsedReason() =>
        $"The run was still going after {MaxElapsedMs} ms and was stopped. "
        + "Raise GRIMOIRE_RUN_MAX_ELAPSED_MS if this source needs longer.";

    /// <summary>The elapsed ceiling as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(MaxElapsedMs);
}

namespace Grimoire.Tasks;

/// <summary>How a tool call ended.</summary>
public enum ToolCallOutcome
{
    /// <summary>The call ran and returned a result.</summary>
    Ok,

    /// <summary>The call was permitted but did not succeed.</summary>
    Failed,

    /// <summary>
    /// The call was denied before it could act — outside the granted set, or outside the wiki.
    /// A refusal is a recorded call, not an absence (FR-011, FR-021).
    /// </summary>
    Refused,
}

/// <summary>
/// One entry per call the agent made, in the order it was made, including refused calls
/// (FR-021). An operator reading the list can tell a run that read before writing from one
/// that wrote blind (SC-010), and a deliberate no-change run from a broken one (SC-011).
/// </summary>
/// <param name="Seq">1-based, strictly increasing, the order the calls were made.</param>
/// <param name="Tool">
/// The tool name as the model named it — including a name outside the granted set, which is
/// how a denied attempt is recorded.
/// </param>
/// <param name="Target">
/// The wiki page path for a granted call; the raw requested target for a refused one.
/// </param>
/// <param name="Outcome">Whether the call succeeded, failed, or was refused.</param>
/// <param name="Detail">Why it failed or was refused.</param>
/// <param name="At">When the call resolved.</param>
public sealed record ToolCall(
    int Seq,
    string Tool,
    string? Target,
    ToolCallOutcome Outcome,
    string? Detail,
    DateTimeOffset At);

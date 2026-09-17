namespace Grimoire.Tasks;

/// <summary>
/// The set of actions the agent may take, recorded on the artifact before it acts
/// (FR-013, constitution II.3). Deny-by-default: anything not named here is refused.
/// </summary>
/// <param name="Tools">
/// The granted tool names. For this feature exactly
/// <c>mcp__wiki__read_page</c> and <c>mcp__wiki__write_page</c> (FR-010, SC-004).
/// </param>
/// <param name="RecordedAt">When the grant was recorded — before the first model call (FR-013).</param>
public sealed record ToolGrant(IReadOnlyList<string> Tools, DateTimeOffset RecordedAt);

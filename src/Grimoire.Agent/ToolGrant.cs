namespace Grimoire.Agent;

/// <summary>
/// The tools a run may use, recorded with it (GUARD-002, GUARD-003).
/// </summary>
/// <remarks>
/// <para>
/// These are the <b>bare</b> names, as the hub serves them and as the grant records them. The CLI
/// namespaces every MCP tool and reports them prefixed; neither spelling is converted in store —
/// <c>HarnessProcess</c> maps before it compares, and it is the only place that knows the prefix
/// (Constitution V.2, data-model.md §ToolGrant).
/// </para>
/// <para>
/// The grant is not a permission list laid over a larger surface: the run is started with every
/// built-in tool switched off, so a tool absent from here does not exist for that run at all
/// (GUARD-001; research.md R-03, R-11).
/// </para>
/// </remarks>
public sealed record ToolGrant(IReadOnlyList<string> ToolNames, DateTimeOffset RecordedAt)
{
    /// <summary>
    /// Reading anything in the wiki, and creating and changing pages, indexes and the log. No
    /// delete and no move: GUARD-002 does not grant them, and a first ingest does not need them.
    /// </summary>
    public static readonly IReadOnlyList<string> ForIngest =
    [
        "list_pages",
        "read_page",
        "write_page",
        "write_index",
        "append_log",
    ];

    public static ToolGrant Ingest(TimeProvider clock) => new(ForIngest, clock.GetUtcNow());

    /// <summary>
    /// Whether a reported tool surface <em>is</em> this grant — the same names, no more and no
    /// fewer. A surface that is not the grant ends the run failed before its first model call
    /// (GUARD-001), so this is equality and not containment.
    /// </summary>
    public bool IsTheSurface(IEnumerable<string> reported) =>
        reported is not null && new HashSet<string>(reported, StringComparer.Ordinal)
            .SetEquals(ToolNames);
}

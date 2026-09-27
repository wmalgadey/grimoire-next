namespace Grimoire.Agent;

/// <summary>
/// The tools a run may use, recorded with it (GUARD-002, GUARD-003).
/// </summary>
/// <remarks>
/// <para>
/// These are the <b>bare</b> names, as the hub serves them and as the grant records them. The CLI
/// namespaces every MCP tool and reports them prefixed; neither spelling is converted in store —
/// <c>AgentTranscript</c> maps before it compares, and it is the only place that knows the prefix
/// (Constitution V.2, data-model.md §ToolGrant).
/// </para>
/// <para>
/// The grant is not a permission list laid over a larger surface: the run is started with every
/// built-in tool switched off, so a tool absent from here does not exist for that run at all
/// (GUARD-001; research.md R-03, R-11).
/// </para>
/// </remarks>
/// <param name="Endpoint">
/// Which of the hub's two MCP endpoints serves this grant's tools — the segment in
/// <c>/mcp/{endpoint}/{runId}</c>. It sits on the grant beside the names so that the grant and the
/// door that serves it are <b>one value</b> and cannot disagree: a run dispatched at the wrong door
/// would be served a surface that is not its grant, and there would be no single place to read what
/// should have been (GUARD-001, GUARD-005, research.md R-06).
/// </param>
public sealed record ToolGrant(IReadOnlyList<string> ToolNames, DateTimeOffset RecordedAt, string Endpoint)
{
    /// <summary>Where an ingest run's five tools are served.</summary>
    public const string Runs = "runs";

    /// <summary>
    /// Where a question's two read tools are served — and <b>only</b> those two. The tools a question
    /// is not granted do not exist at this endpoint at all: there is no flag that would turn them on
    /// and no name that would reach them, which is DEC-011's deny-by-default by construction rather
    /// than an allow-list over a larger surface (GUARD-005).
    /// </summary>
    public const string Questions = "questions";

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

    /// <summary>
    /// Reading anything in the wiki, and <b>nothing else</b>: no page, index or log written, nothing
    /// deleted and nothing moved (GUARD-005).
    /// </summary>
    /// <remarks>
    /// The same two tools an ingest run has — same names, same arguments, same answers, serving the
    /// same <c>IWikiStore</c>. Nothing about reading the wiki is different for a question; what is
    /// different is that these are all there is.
    /// </remarks>
    public static readonly IReadOnlyList<string> ForQuestion =
    [
        "list_pages",
        "read_page",
    ];

    public static ToolGrant Ingest(TimeProvider clock) => new(ForIngest, clock.GetUtcNow(), Runs);

    /// <summary>The grant for a question's run, and the door that serves exactly it (GUARD-005).</summary>
    public static ToolGrant Question(TimeProvider clock) => new(ForQuestion, clock.GetUtcNow(), Questions);

    /// <summary>
    /// Whether a reported tool surface <em>is</em> this grant — the same names, no more and no
    /// fewer. A surface that is not the grant ends the run failed before its first model call
    /// (GUARD-001), so this is equality and not containment.
    /// </summary>
    public bool IsTheSurface(IEnumerable<string> reported) =>
        reported is not null && new HashSet<string>(reported, StringComparer.Ordinal)
            .SetEquals(ToolNames);
}

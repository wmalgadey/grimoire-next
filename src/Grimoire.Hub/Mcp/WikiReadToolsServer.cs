using System.ComponentModel;
using Grimoire.Wiki;
using ModelContextProtocol.Server;

namespace Grimoire.Hub.Mcp;

/// <summary>
/// Reading the wiki, once. Both tool surfaces call these: the five-tool one an ingest run is served
/// and the two-tool one a question's run is served (GUARD-002, GUARD-005).
/// </summary>
/// <remarks>
/// The bodies live here so that reading the wiki is one implementation and not two. What is
/// duplicated between the two surfaces is the attributed method that exposes it — about a dozen
/// lines, carried openly in plan.md §Complexity Tracking rather than argued away, because it is what
/// makes a question's grant deny-by-default <b>by construction</b>: the tools it is not granted are
/// not registered at its endpoint at all (DEC-011, research.md R-06).
/// </remarks>
internal static class WikiReads
{
    public static async Task<object> ListPagesAsync(IWikiStore wiki, CancellationToken cancellationToken) =>
        new { paths = await wiki.ListAsync(cancellationToken).ConfigureAwait(false) };

    public static async Task<object> ReadPageAsync(
        IWikiStore wiki, string path, CancellationToken cancellationToken)
    {
        try
        {
            var content = await wiki.ReadAsync(path, cancellationToken).ConfigureAwait(false);

            return content is null
                ? Refused("not-found", $"there is no \"{path}\" in the wiki")
                : new { path, content };
        }
        catch (OutsideWikiException outside)
        {
            return Refused("outside-wiki", outside.Message);
        }
    }

    public static object Refused(string reason, string message) => new { error = reason, message };
}

/// <summary>
/// The two tools a question's run may use, served over MCP at <c>/mcp/questions/{runId}</c>
/// (GUARD-005).
/// </summary>
/// <remarks>
/// <b>What is not here does not exist for that run.</b> <c>write_page</c>, <c>write_index</c> and
/// <c>append_log</c> are not registered on this type, so there is no flag that would turn one on and
/// no name that would reach one — which is why the wiki is byte for byte what it was after a question
/// has been answered, by construction and not because the instruction asked nicely.
/// <para>
/// GUARD-001 then guards it for free: <c>system/init</c> reports the run's whole tool surface and the
/// comparison is by equality, so a question's run served anything beyond these two ends failed before
/// its first model call.
/// </para>
/// <para>
/// It takes no <c>PageProducer</c> and no clock. Those are for stamping what is written (WIKI-002), and
/// nothing here writes — a question's run is never attributed to anything in the wiki, because it puts
/// nothing there.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class WikiReadToolsServer(IWikiStore wiki)
{
    /// <summary>The bare names this server serves, in the order the grant records them.</summary>
    public static IReadOnlyList<string> ServedNames => NamesOf(typeof(WikiReadToolsServer));

    [McpServerTool(Name = "list_pages")]
    [Description("Every path in the wiki, relative to its root.")]
    public Task<object> ListPagesAsync(CancellationToken cancellationToken) =>
        WikiReads.ListPagesAsync(wiki, cancellationToken);

    [McpServerTool(Name = "read_page")]
    [Description("One file's full text, frontmatter included.")]
    public Task<object> ReadPageAsync(
        [Description("Path relative to the wiki root.")] string path,
        CancellationToken cancellationToken) =>
        WikiReads.ReadPageAsync(wiki, path, cancellationToken);

    /// <summary>
    /// The bare tool names a surface exposes, read off its own attributes. One reading for both
    /// surfaces, so neither can drift from what it actually serves.
    /// </summary>
    internal static IReadOnlyList<string> NamesOf(Type surface) =>
    [
        .. surface
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).FirstOrDefault())
            .OfType<McpServerToolAttribute>()
            .Select(a => a.Name!)
            .Where(n => n is not null),
    ];
}

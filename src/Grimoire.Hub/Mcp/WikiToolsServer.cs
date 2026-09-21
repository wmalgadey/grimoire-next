using System.ComponentModel;
using Grimoire.Wiki;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace Grimoire.Hub.Mcp;

/// <summary>Which run a tool call belongs to, read from the endpoint it arrived at.</summary>
/// <remarks>
/// The run identifier is in the path — <c>/mcp/runs/{runId}</c> — which is how a tool call is
/// attributed to its run. That is addressing, not authorisation: the endpoint is unauthenticated
/// and bound to loopback, because <c>docs/product.md</c> §2 puts Grimoire inside a network the
/// user trusts. The first feature that puts it on an untrusted network has to revisit this.
/// </remarks>
public sealed class RunAddress(IHttpContextAccessor accessor, string model)
{
    /// <summary>
    /// Which run this call belongs to. A call that arrives without a readable run identifier is
    /// refused rather than attributed to an empty one: the identifier is what names the run in the
    /// generation record (WIKI-002) and in the wiki's log (RUNS-005), so a wrong one is worse than
    /// no write at all.
    /// </summary>
    public Guid RunId =>
        accessor.HttpContext?.Request.RouteValues["runId"] is string id && Guid.TryParse(id, out var runId)
            ? runId
            : throw new InvalidOperationException("this tool call carries no readable run identifier");

    /// <summary>
    /// Who generated a page: Grimoire, and the model the run was served by. This is the whole of
    /// what Grimoire writes into the wiki (WIKI-002, Constitution V.1).
    /// </summary>
    /// <remarks>
    /// The <c>&lt;producer&gt;/&lt;version&gt;</c> form of OKF 0.2 §7, which admits three spellings
    /// of an actor and no free text; the standard's own example is
    /// <c>reference_agent/gemini-2.5-pro</c>. The root index declares <c>okf_version: "0.2"</c>, so
    /// a record in any other shape is a claim the wiki does not honour.
    /// <para>
    /// The run identifier is deliberately not here. An actor is one actor, and the page was
    /// produced by the agent rather than by a run; what the run did is in the log entry, which
    /// names every file it touched, and <see cref="GenerationRecord.At"/> dates the page.
    /// </para>
    /// </remarks>
    public string GeneratedBy => $"grimoire/{model}";
}

/// <summary>
/// The five tools a run may use, served over MCP at the run's own endpoint. This list is the
/// grant, and the grant is the agent's entire tool surface (GUARD-001, GUARD-002).
/// </summary>
/// <remarks>
/// <c>write_page</c> stamps; <c>write_index</c> and <c>append_log</c> do not, because indexes and
/// the log are not pages (data-model.md). Nothing here removes, moves, reverts or commits: the
/// port has no such call, and a first ingest needs none.
/// </remarks>
[McpServerToolType]
public sealed class WikiToolsServer(IWikiStore wiki, RunAddress run, TimeProvider clock)
{
    /// <summary>The bare names this server serves, in the order the grant records them.</summary>
    public static IReadOnlyList<string> ServedNames =>
    [
        .. typeof(WikiToolsServer)
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).FirstOrDefault())
            .OfType<McpServerToolAttribute>()
            .Select(a => a.Name!)
            .Where(n => n is not null),
    ];

    [McpServerTool(Name = "list_pages")]
    [Description("Every path in the wiki, relative to its root.")]
    public async Task<object> ListPagesAsync(CancellationToken cancellationToken) =>
        new { paths = await wiki.ListAsync(cancellationToken).ConfigureAwait(false) };

    [McpServerTool(Name = "read_page")]
    [Description("One file's full text, frontmatter included.")]
    public async Task<object> ReadPageAsync(
        [Description("Path relative to the wiki root.")] string path,
        CancellationToken cancellationToken)
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

    [McpServerTool(Name = "write_page")]
    [Description("Create a page or replace it whole. Grimoire records who generated it and when.")]
    public async Task<object> WritePageAsync(
        [Description("Path relative to the wiki root.")] string path,
        [Description("The page's full text, frontmatter included.")] string content,
        CancellationToken cancellationToken)
    {
        var record = new GenerationRecord(run.GeneratedBy, clock.GetUtcNow());
        var stamped = ProvenanceStamp.Apply(content, record);

        if (stamped.Page is null)
        {
            // The place for the record cannot be read: the write fails and the agent is told why
            // (WIKI-002). Nothing else about the page is judged.
            return Refused("frontmatter-unreadable", stamped.Error!);
        }

        try
        {
            await wiki.WriteAsync(path, stamped.Page, cancellationToken).ConfigureAwait(false);
            return new { written = path, generated = new { by = record.By, at = record.At } };
        }
        catch (OutsideWikiException outside)
        {
            return Refused("outside-wiki", outside.Message);
        }
    }

    [McpServerTool(Name = "write_index")]
    [Description("Create or replace a section index or the root index. Carries no generation record.")]
    public async Task<object> WriteIndexAsync(
        [Description("Path relative to the wiki root.")] string path,
        [Description("The index's full text.")] string content,
        CancellationToken cancellationToken)
    {
        try
        {
            await wiki.WriteAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new { written = path };
        }
        catch (OutsideWikiException outside)
        {
            return Refused("outside-wiki", outside.Message);
        }
    }

    [McpServerTool(Name = "append_log")]
    [Description("Add an entry to the wiki's log. Carries no generation record.")]
    public async Task<object> AppendLogAsync(
        [Description("The entry, which must identify this run.")] string entry,
        CancellationToken cancellationToken)
    {
        await wiki.AppendLogAsync(entry, cancellationToken).ConfigureAwait(false);
        return new { appended = WikiFile.Log };
    }

    private static object Refused(string reason, string message) => new { error = reason, message };
}

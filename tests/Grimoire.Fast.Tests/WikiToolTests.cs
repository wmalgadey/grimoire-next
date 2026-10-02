using System.Text.Json;
using Grimoire.Hub.Mcp;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the two tools that write without a generation record do — <c>write_index</c> and
/// <c>append_log</c> (GUARD-002: an ingest run creates and changes pages, indexes and the log).
/// </summary>
/// <remarks>
/// <para>
/// <c>ToolGrantTests</c> proves the names are granted and served; a name served by a handler that
/// writes nothing would pass there. This is what calling them does to the wiki, over the in-memory
/// store and without a server: which tools reach a run's door is the Contract suite's business
/// (<c>WikiToolDoorTests</c>), what a tool does once called is not.
/// </para>
/// <para>
/// <c>write_page</c> stamps, and what it stamps is <c>ProvenanceStampTests</c>.
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class WikiToolTests
{
    private readonly InMemoryWikiStore wiki = new();

    private readonly WikiToolsServer tools;

    public WikiToolTests() =>
        tools = new WikiToolsServer(
            wiki, new PageProducer(FastHub.Model), FastSuite.Clock());

    [Fact]
    [Trait("req", "GUARD-002")]
    public async Task WriteIndex_WritesTheIndex()
    {
        await tools.WriteIndexAsync("people/index.md", "# People\n", TestContext.Current.CancellationToken);

        // As given: an index carries no generation record, so nothing is added to it.
        Assert.Equal("# People\n", wiki.Files["people/index.md"]);
    }

    [Fact]
    [Trait("req", "GUARD-002")]
    public async Task WriteIndex_IsRefused_WhenThePathIsAPage()
    {
        // Through this tool a page would be written with no record of who generated it, which is the
        // whole of WIKI-002 — so the agent is told to use write_page, and the wiki is untouched.
        var answer = await tools.WriteIndexAsync(
            "people/ada.md", "# Ada\n", TestContext.Current.CancellationToken);

        Assert.Contains("not-an-index", JsonSerializer.Serialize(answer), StringComparison.Ordinal);
        Assert.Empty(wiki.Files);
    }

    [Fact]
    [Trait("req", "GUARD-002")]
    public async Task AppendLog_AddsTheEntryToTheLog()
    {
        await tools.AppendLogAsync("## run 8f3c1d4e: added people/ada.md", TestContext.Current.CancellationToken);

        Assert.Contains(
            "## run 8f3c1d4e: added people/ada.md", wiki.Files[InMemoryWikiStore.LogPath], StringComparison.Ordinal);
    }
}

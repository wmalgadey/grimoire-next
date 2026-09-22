using Grimoire.Wiki;
using YamlDotNet.RepresentationModel;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The generation record: the one thing Grimoire writes into the wiki (WIKI-002, Constitution V.1).
/// The four behaviours of data-model.md, and nothing else about the page is judged.
/// </summary>
[Trait("level", "fast")]
[Trait("req", "WIKI-002")]
public sealed class ProvenanceStampTests
{
    private static readonly GenerationRecord Record = new(
        By: "Grimoire, claude-opus-4-5-20251101, run 8f3c1d4e-0000-0000-0000-000000000001",
        At: new DateTimeOffset(2026, 9, 20, 10, 6, 31, TimeSpan.Zero));

    private static string Page(string frontmatter, string body = "The body.") =>
        $"---\n{frontmatter}\n---\n\n{body}\n";

    [Fact]
    public void WritePage_AddsTheRecord_WhenThePageHasNoPlaceForIt()
    {
        var result = ProvenanceStamp.Apply(Page("type: Recipe\nsources:\n  - resource: ada.md"), Record);

        Assert.Null(result.Error);
        Assert.Contains("generated:", result.Page);
        Assert.Contains($"  at: {Record.At:yyyy-MM-ddTHH:mm:ssZ}", result.Page);
        Assert.Contains($"  by: {Record.By}", result.Page);
    }

    [Fact]
    public void WritePage_KeepsEverythingElseAsTheAgentWroteIt_WhenTheRecordIsAdded()
    {
        var result = ProvenanceStamp.Apply(Page("type: Recipe\nsources:\n  - resource: ada.md"), Record);

        // Nothing else about the page is judged, and nothing else about it is rewritten: the
        // agent's own text comes back byte for byte (Constitution V.1).
        Assert.Contains("type: Recipe\nsources:\n  - resource: ada.md", result.Page);
        Assert.EndsWith("---\n\nThe body.\n", result.Page);
    }

    [Fact]
    public void WritePage_ReplacesAgentValues_WhenTheRecordIsAlreadyThere()
    {
        var supplied = "type: Recipe\ngenerated:\n  by: the agent itself\n  at: 1999-01-01T00:00:00Z\n  extra: whatever";

        var result = ProvenanceStamp.Apply(Page(supplied), Record);

        Assert.Null(result.Error);
        Assert.DoesNotContain("the agent itself", result.Page);
        Assert.DoesNotContain("1999-01-01", result.Page);
        Assert.DoesNotContain("extra:", result.Page);
        Assert.Contains($"  by: {Record.By}", result.Page);
        Assert.Contains("type: Recipe", result.Page);
    }

    [Fact]
    public void WritePage_NamesTheUpdatingRun_WhenThePageIsUpdated()
    {
        var first = new GenerationRecord("run one", new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero));
        var stamped = ProvenanceStamp.Apply(Page("type: Recipe"), first).Page!;

        var updated = ProvenanceStamp.Apply(stamped, Record);

        Assert.DoesNotContain("run one", updated.Page);
        Assert.Contains($"  by: {Record.By}", updated.Page);
    }

    [Theory]
    [InlineData("type: Recipe\n  bad: indentation\n   worse: still", "frontmatter that is not YAML")]
    [InlineData("type: Recipe\ngenerated: a plain string", "a generated that is a scalar")]
    [InlineData("type: Recipe\ngenerated:\n  - by: a list", "a generated that is a list")]
    public void WritePage_Fails_WhenTheRecordsPlaceCannotBeRead(string frontmatter, string why)
    {
        var result = ProvenanceStamp.Apply(Page(frontmatter), Record);

        Assert.Null(result.Page);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error!);
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Fact]
    public void WritePage_ReplacesAgentValues_WhenTheFrontmatterIsIndented()
    {
        // The key is found where the parser says it is, not by a search anchored at column 0. A
        // page whose record was not found would get a second `generated` block, which is two of
        // them on one page.
        var supplied = "  type: Recipe\n  generated:\n    by: the agent itself\n    at: 1999-01-01T00:00:00Z";

        var result = ProvenanceStamp.Apply(Page(supplied), Record);

        Assert.Null(result.Error);
        Assert.Equal(1, Occurrences(result.Page!, "generated:"));
        Assert.DoesNotContain("the agent itself", result.Page);
    }

    [Fact]
    public void WritePage_ReplacesAgentValues_WhenCommentsSitAboveTheRecord()
    {
        var supplied = "type: Recipe\n# a comment the agent wrote\ngenerated:\n  by: the agent itself\n  at: 1999-01-01T00:00:00Z\nokf_version: \"0.2\"";

        var result = ProvenanceStamp.Apply(Page(supplied), Record);

        Assert.Null(result.Error);
        Assert.Equal(1, Occurrences(result.Page!, "generated:"));
        Assert.Contains("# a comment the agent wrote", result.Page, StringComparison.Ordinal);
        Assert.Contains("okf_version:", result.Page, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle) =>
        text.Split(needle, StringSplitOptions.None).Length - 1;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WritePage_AddsTheRecord_WhenTheFrontmatterIsEmpty(string frontmatter)
    {
        // Nothing in it is not the same as unreadable: the page has a place for the record and
        // simply has nothing else in it yet.
        var result = ProvenanceStamp.Apply(Page(frontmatter), Record);

        Assert.Null(result.Error);
        Assert.Equal(1, Occurrences(result.Page!, "generated:"));
        Assert.Contains($"  by: {Record.By}", result.Page, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePage_Fails_WhenThePageHasNoFrontmatterAtAll()
    {
        var result = ProvenanceStamp.Apply("Just a body, no frontmatter.\n", Record);

        Assert.Null(result.Page);

        // Told why, not merely refused. An empty reason is nothing the agent can act on, and
        // WIKI-002 asks for the reason and not only for the refusal.
        Assert.Contains("frontmatter", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePage_Fails_WhenSomethingStandsAboveTheOpeningFence()
    {
        // Frontmatter is the block the page opens with. A --- further down is a horizontal rule
        // in the body, and reading from there would stamp the record into the agent's prose.
        var result = ProvenanceStamp.Apply("A heading first.\n---\ntype: Recipe\n---\n\nThe body.\n", Record);

        Assert.Null(result.Page);
        Assert.Contains("frontmatter", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePage_Fails_WhenTheFrontmatterIsNeverClosed()
    {
        var result = ProvenanceStamp.Apply("---\ntype: Recipe\n\nThe body.\n", Record);

        Assert.Null(result.Page);
        Assert.Contains("frontmatter", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePage_Fails_WhenTheFrontmatterIsNotAMappingOfKeysToValues()
    {
        var result = ProvenanceStamp.Apply(Page("- one\n- two"), Record);

        Assert.Null(result.Page);
        Assert.Contains("mapping", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePage_Fails_WhenTheRecordsPlaceIsNotWhereTheTextSaysItIs()
    {
        // The parser reports `generated` on the line the flow mapping occupies, and that line
        // does not read as the key. Stamping there anyway would rewrite the whole line; adding a
        // second `generated` would leave the page with two of them. So the write fails instead,
        // which is what "a place that cannot be read" means (research.md R-07).
        var result = ProvenanceStamp.Apply(
            Page("{type: Recipe, generated: {by: the agent itself, at: 1999-01-01T00:00:00Z}}"),
            Record);

        Assert.Null(result.Page);
        Assert.Contains("generated", result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("grimoire/claude: opus 5")]
    [InlineData("\"grimoire/claude-opus-5\"")]
    [InlineData("- grimoire/claude-opus-5")]
    public void WritePage_WritesAnActorAYamlReaderReadsBack_WhenPlainYamlWouldNot(string actor)
    {
        // The actor is not a constant of ours: it is `grimoire/<model>` and the model name is
        // configuration, so whatever is configured reaches `generated.by`. A colon, a leading
        // quote and a leading `-` are three spellings a YAML reader would not give back as they
        // went in — a colon is a mapping, a quote opens a quoted scalar, a `-` opens a sequence.
        // What WIKI-002 asks for is that the record says who generated the page, and a value the
        // reader cannot give back does not say it.
        var result = ProvenanceStamp.Apply(Page("type: Recipe"), new GenerationRecord(actor, Record.At));

        Assert.Null(result.Error);
        Assert.Equal(actor, GeneratedBy(result.Page!));
    }

    /// <summary>The <c>generated.by</c> of a stamped page, as a YAML reader gives it back.</summary>
    private static string GeneratedBy(string page)
    {
        var closing = page.IndexOf("\n---", 4, StringComparison.Ordinal);
        var frontmatter = page[4..closing];

        var read = new YamlStream();
        read.Load(new StringReader(frontmatter));

        var generated = (YamlMappingNode)((YamlMappingNode)read.Documents[0].RootNode)["generated"];
        return ((YamlScalarNode)generated["by"]).Value!;
    }

    [Fact]
    public void WritePage_ReplacesAgentValues_WhenTheRecordOpensTheFrontmatter()
    {
        // The record on the very first line of the frontmatter is the boundary case of "where the
        // parser says it is": index zero is a place, not the absence of one.
        var supplied = "generated:\n  by: the agent itself\n  at: 1999-01-01T00:00:00Z\ntype: Recipe";

        var result = ProvenanceStamp.Apply(Page(supplied), Record);

        Assert.Null(result.Error);
        Assert.Equal(1, Occurrences(result.Page!, "generated:"));
        Assert.DoesNotContain("the agent itself", result.Page);
        Assert.Contains($"  by: {Record.By}", result.Page, StringComparison.Ordinal);
        Assert.Contains("type: Recipe", result.Page, StringComparison.Ordinal);
    }
}

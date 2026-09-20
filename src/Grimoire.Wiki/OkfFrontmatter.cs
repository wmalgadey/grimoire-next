using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Grimoire.Wiki;

/// <summary>
/// A page's YAML frontmatter, as far as Grimoire reads it.
/// </summary>
/// <remarks>
/// <para>
/// Of OKF 0.2, only the parts research.md R-07 names apply at all, and of those only
/// <c>generated</c> is Grimoire's: it is the one key Grimoire writes (WIKI-002) and therefore the
/// one it has to find. <c>type</c>, <c>sources</c> and <c>okf_version</c> are the agent's, and
/// Grimoire judges nothing about them — an accessor for them would be a mechanism with no
/// consumer (Constitution II.1). Nothing else of the standard is built.
/// </para>
/// <para>
/// The frontmatter is parsed to answer two questions and no more: does it read as YAML at all, and
/// is <c>generated</c> a place the record can go. The page is then edited as <em>text</em>, so
/// that everything the agent wrote comes back byte for byte — re-serialising the YAML would
/// reformat the agent's own words, and into the wiki Grimoire writes only facts about the run
/// (Constitution V.1).
/// </para>
/// </remarks>
internal sealed class OkfFrontmatter
{
    private const string Fence = "---";
    private const string GeneratedKey = "generated";

    private OkfFrontmatter(IReadOnlyList<string> lines, string rest, int generatedAt, int generatedEnd)
    {
        Lines = lines;
        Rest = rest;
        GeneratedAt = generatedAt;
        GeneratedEnd = generatedEnd;
    }

    /// <summary>The frontmatter's lines, without the fences.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>Everything from the closing fence onwards, untouched.</summary>
    public string Rest { get; }

    /// <summary>Index into <see cref="Lines"/> where <c>generated</c> begins, or -1 where it is absent.</summary>
    public int GeneratedAt { get; }

    /// <summary>Index into <see cref="Lines"/> just past <c>generated</c>, or -1 where it is absent.</summary>
    public int GeneratedEnd { get; }

    /// <summary>
    /// Reads a page's frontmatter, or explains why it cannot be read in a way the agent can act on.
    /// </summary>
    public static OkfFrontmatter? Read(string page, out string? error)
    {
        error = null;

        var lines = page.Split('\n');

        if (lines.Length == 0 || lines[0].TrimEnd('\r') != Fence)
        {
            error = "the page has no YAML frontmatter: it must open with a line reading ---";
            return null;
        }

        var closing = Array.FindIndex(lines, 1, l => l.TrimEnd('\r') == Fence);
        if (closing < 0)
        {
            error = "the page's frontmatter is never closed: a line reading --- is missing";
            return null;
        }

        var frontmatter = lines[1..closing];
        var rest = string.Join('\n', lines[closing..]);

        var root = Parse(string.Join('\n', frontmatter), out error);
        if (root is null)
        {
            return null;
        }

        var generated = root.Children.Keys
            .OfType<YamlScalarNode>()
            .FirstOrDefault(k => k.Value == GeneratedKey);

        if (generated is null)
        {
            return new OkfFrontmatter(frontmatter, rest, -1, -1);
        }

        if (root.Children[generated] is not YamlMappingNode)
        {
            // "A place that cannot be read" (research.md R-07): the key is there but it is not
            // somewhere a record of two fields can go.
            error = $"{GeneratedKey} is present but is not a mapping, so the generation record has nowhere to go";
            return null;
        }

        var at = Span(frontmatter, generated.Start, out var end);
        if (at < 0)
        {
            // The parser found the key and the text does not agree. Adding a second `generated`
            // would leave a page with two of them, which is worse than refusing the write: what
            // the check cannot read, it fails on rather than skips.
            error = $"{GeneratedKey} was read from the frontmatter but could not be found in it to be replaced";
            return null;
        }

        return new OkfFrontmatter(frontmatter, rest, at, end);
    }

    /// <summary>Parses the frontmatter, or says why it does not read as YAML.</summary>
    private static YamlMappingNode? Parse(string yaml, out string? error)
    {
        error = null;

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));

            if (stream.Documents.Count == 0)
            {
                return [];
            }

            if (stream.Documents[0].RootNode is YamlMappingNode mapping)
            {
                return mapping;
            }

            error = "the page's frontmatter does not read as a mapping of keys to values";
            return null;
        }
        catch (YamlException failure)
        {
            error = $"the page's frontmatter does not read as YAML: {failure.Message}";
            return null;
        }
    }

    /// <summary>
    /// Which lines the <c>generated</c> entry occupies: its own, and every line after it that is
    /// blank or indented further than the key is, up to the next key at the key's own indent.
    /// </summary>
    /// <remarks>
    /// The key's line comes from the parser rather than from a search, so a frontmatter whose
    /// whole mapping is indented, or that carries comments above the key, is handled the same as
    /// the ordinary case. Returns -1 where the parser's position does not describe the text,
    /// which the caller turns into a refused write rather than a second <c>generated</c> block.
    /// </remarks>
    private static int Span(string[] lines, Mark key, out int end)
    {
        var at = (int)key.Line - 1;
        end = -1;

        if (at < 0 || at >= lines.Length || !lines[at].TrimStart().StartsWith($"{GeneratedKey}:", StringComparison.Ordinal))
        {
            return -1;
        }

        var indent = lines[at].Length - lines[at].TrimStart().Length;

        end = at + 1;
        while (end < lines.Length && IsInsideTheEntry(lines[end], indent))
        {
            end++;
        }

        return at;
    }

    private static bool IsInsideTheEntry(string line, int indent) =>
        line.Trim().Length == 0 || line.Length - line.TrimStart().Length > indent;
}

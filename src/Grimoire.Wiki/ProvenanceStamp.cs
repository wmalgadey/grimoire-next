namespace Grimoire.Wiki;

/// <summary>
/// Who generated a page and when. The only thing Grimoire writes into the wiki
/// (WIKI-002, Constitution V.1).
/// </summary>
/// <param name="By">An actor, in one of the three forms OKF 0.2 §7 admits.</param>
/// <param name="At">The write time.</param>
public sealed record GenerationRecord(string By, DateTimeOffset At);

/// <summary>What stamping a page produced: the page to write, or why the write must fail.</summary>
public sealed record StampResult
{
    private StampResult(string? page, string? error)
    {
        Page = page;
        Error = error;
    }

    public string? Page { get; }

    /// <summary>A message the agent can act on, where the record's place cannot be read.</summary>
    public string? Error { get; }

    public static StampResult Of(string page) => new(page, null);

    public static StampResult Failed(string error) => new(null, error);
}

/// <summary>
/// Adds the generation record to a page, replaces one the agent supplied, or fails where the
/// record has nowhere it can go. The three cases of data-model.md, and nothing else about the page
/// is judged (WIKI-002).
/// </summary>
public static class ProvenanceStamp
{
    public static StampResult Apply(string page, GenerationRecord record)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(record);

        var frontmatter = OkfFrontmatter.Read(page, out var error);
        if (frontmatter is null)
        {
            return StampResult.Failed(error!);
        }

        var lines = frontmatter.Lines.ToList();
        var written = Render(record);

        if (frontmatter.GeneratedAt < 0)
        {
            // No place for the record: one is added, and the write succeeds.
            lines.AddRange(written);
        }
        else
        {
            // A record the agent supplied, in whatever shape: replaced with Grimoire's values,
            // whole. On an update that means the record names the run that updated the page.
            lines.RemoveRange(frontmatter.GeneratedAt, frontmatter.GeneratedEnd - frontmatter.GeneratedAt);
            lines.InsertRange(frontmatter.GeneratedAt, written);
        }

        return StampResult.Of($"---\n{string.Join('\n', lines)}\n{frontmatter.Rest}");
    }

    private static string[] Render(GenerationRecord record) =>
    [
        "generated:",
        $"  by: {Scalar(record.By)}",
        $"  at: {Scalar(record.At.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture))}",
    ];

    /// <summary>
    /// A YAML scalar, quoted only where it has to be. The wiki stays a document a person reads, so
    /// quotes are not added where they buy nothing.
    /// </summary>
    private static string Scalar(string value)
    {
        var plain =
            value.Length > 0
            && !char.IsWhiteSpace(value[0])
            && !char.IsWhiteSpace(value[^1])
            && !value.Contains(": ", StringComparison.Ordinal)
            && !value.Contains(" #", StringComparison.Ordinal)
            && !value.Contains('\n', StringComparison.Ordinal)
            && !value.EndsWith(':')
            && "-?:,[]{}#&*!|>'\"%@`".IndexOf(value[0]) < 0;

        return plain ? value : $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

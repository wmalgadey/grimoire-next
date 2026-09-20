namespace Grimoire.Wiki;

/// <summary>
/// Raised where a path would leave the wiki — absolute, or climbing out with <c>..</c>. This is
/// GUARD-001's edge case: the agent cannot reach outside the wiki because no granted tool does.
/// </summary>
public sealed class OutsideWikiException(string path)
    : Exception($"\"{path}\" is outside the wiki")
{
    public string Path { get; } = path;
}

/// <summary>The files the wiki reserves by name. Neither is a page (data-model.md).</summary>
public static class WikiFile
{
    /// <summary>The wiki's record of what each run changed and why.</summary>
    public const string Log = "log.md";

    /// <summary>The name a section index and the root index both carry.</summary>
    public const string Index = "index.md";
}

/// <summary>
/// The only way into the wiki. Its one adapter is <c>FileSystemWikiStore</c>, which is the only
/// place the filesystem is touched (Constitution V.2).
/// </summary>
/// <remarks>
/// <para>
/// Reading, creating and changing only. There is no remove, no move, no revert and no commit — not
/// as a refusal at runtime but as an absence at the port, because WIKI-003 says a failed run's
/// writes stay where they are and <c>docs/product.md</c> §4 leaves undo to the wiki's own history.
/// A capability that does not exist cannot be reached by mistake.
/// </para>
/// <para>
/// Pages, indexes and the log are all files here. Which of them carries a generation record is not
/// this port's business — it is the tool server's, because indexes and the log are not pages
/// (data-model.md).
/// </para>
/// </remarks>
public interface IWikiStore
{
    /// <summary>Every path in the wiki, relative to its root.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One file's full text, frontmatter included, or null where there is no such file.</summary>
    Task<string?> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Create the file or replace it whole.</summary>
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>Add an entry to the wiki's <c>log.md</c>, after whatever is already there.</summary>
    Task AppendLogAsync(string entry, CancellationToken cancellationToken);
}

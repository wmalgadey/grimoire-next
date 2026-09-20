namespace Grimoire.Wiki.Adapters;

/// <summary>
/// The wiki as files in a directory. The only place the filesystem is touched
/// (Constitution V.2).
/// </summary>
/// <remarks>
/// Nothing here removes, reverts or commits anything, ever: a failed run's writes stay exactly
/// where the agent left them (WIKI-003), and undo is the wiki's own version history, which belongs
/// to the user (<c>docs/product.md</c> §4).
/// </remarks>
public sealed class FileSystemWikiStore : IWikiStore
{
    private readonly string root;

    public FileSystemWikiStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> paths =
        [
            .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal),
        ];

        return Task.FromResult(paths);
    }

    public async Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var file = Resolve(path);

        return File.Exists(file)
            ? await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var file = Resolve(path);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendLogAsync(string entry, CancellationToken cancellationToken)
    {
        var file = Resolve(WikiFile.Log);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.AppendAllTextAsync(file, entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The file this path names inside the wiki, or <see cref="OutsideWikiException"/> where it
    /// names something outside it. Both spellings are refused: an absolute path, and a relative
    /// one that climbs out with <c>..</c>. The check is on the resolved path rather than on the
    /// text, so a path that only looks harmless is caught too.
    /// </summary>
    private string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0 || Path.IsPathRooted(path))
        {
            throw new OutsideWikiException(path);
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, path)));

        if (full.Length <= root.Length
            || !full.StartsWith(root, StringComparison.Ordinal)
            || full[root.Length] != Path.DirectorySeparatorChar)
        {
            throw new OutsideWikiException(path);
        }

        return full;
    }
}

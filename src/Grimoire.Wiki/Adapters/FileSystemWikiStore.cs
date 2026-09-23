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
            .. Directory.EnumerateFiles(root, "*", ListingLeavesLinksAlone)
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                // A wiki is normally a git repository — scripts/run-hub.sh makes one — and its
                // .git holds hundreds of files that are not pages. Listing them would bury the
                // wiki in its own bookkeeping, and every dotted name is the same kind of thing.
                .Where(p => !IsHidden(p))
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

        // ensure the log files always ends with a single newline, so new log entries are separated by a blank line
        await File.AppendAllTextAsync(file, entry.TrimEnd('\n') + "\n\n", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The file this path names inside the wiki, or <see cref="OutsideWikiException"/> where it
    /// names something outside it. Four spellings are refused: an absolute path, a relative one
    /// that climbs out with <c>..</c>, one that names a dotted file or directory, and one that
    /// leaves through a symbolic link. The check is on the resolved path rather than on the text,
    /// so a path that only looks harmless is caught too.
    /// </summary>
    private string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0 || Path.IsPathRooted(path) || IsHidden(path))
        {
            throw new OutsideWikiException(path);
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, path)));

        if (!IsInside(root, full))
        {
            throw new OutsideWikiException(path);
        }

        RefuseAPathThatLeavesThroughALink(path);

        return full;
    }

    /// <summary>
    /// Whether a path names a dotted file or directory anywhere along it. <c>.git</c> is the one
    /// that matters — <c>scripts/run-hub.sh</c> puts the wiki under git, so the repository that
    /// holds the only undo there is sits inside the very directory the granted tools write to, and
    /// nothing in <c>Resolve</c>'s containment check keeps a <c>write_page(".git/HEAD", …)</c> out
    /// of it. A dotted name is not a page in any case (GUARD-001, data-model.md).
    /// </summary>
    private static bool IsHidden(string path) =>
        path.Split('/', '\\').Any(segment => segment.Length > 1 && segment[0] == '.');

    /// <summary>Whether a resolved path lies strictly inside a resolved directory.</summary>
    private static bool IsInside(string directory, string full) =>
        full.Length > directory.Length
        && full.StartsWith(directory, StringComparison.Ordinal)
        // A filesystem root keeps its separator — TrimEndingDirectorySeparator leaves "/" and
        // "C:\\" as they are — so the child begins straight after it rather than after a
        // separator of its own.
        && (directory.EndsWith(Path.DirectorySeparatorChar) || full[directory.Length] == Path.DirectorySeparatorChar);

    /// <summary>
    /// How the wiki is enumerated: into real directories only. <c>AttributesToSkip</c> covers the
    /// entry itself and the recursion, so a linked directory is neither listed nor descended into.
    /// Without it <c>list_pages</c> would answer with files from outside the wiki that
    /// <see cref="ReadAsync"/> and <see cref="WriteAsync"/> both refuse to touch.
    /// </summary>
    private static readonly EnumerationOptions ListingLeavesLinksAlone = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    /// <summary>
    /// Refuses a path that reaches outside the wiki through a symbolic link.
    /// </summary>
    /// <remarks>
    /// <c>GetFullPath</c> is textual: it collapses <c>..</c> and nothing else, so a link inside the
    /// wiki pointing anywhere at all passes the containment check while reading and writing the
    /// link's target. Every component is therefore walked from the wiki's own real path downwards,
    /// each link resolved as it is reached, and the result checked again. The wiki's root is
    /// resolved too, because a wiki that is itself reached through a link — <c>/tmp</c> on macOS is
    /// one — would otherwise put every path in it outside itself.
    /// </remarks>
    private void RefuseAPathThatLeavesThroughALink(string path)
    {
        var real = RealPathOf(root) ?? root;
        var inside = real;

        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            inside = Path.Combine(inside, segment);

            if (RealPathOf(inside) is { } target && target != inside)
            {
                if (!IsInside(real, target))
                {
                    throw new OutsideWikiException(path);
                }

                inside = target;
            }
        }
    }

    /// <summary>
    /// What this path really is once every link on it is followed, or null where nothing is there.
    /// A path that does not exist yet is not a link and cannot lead anywhere.
    /// </summary>
    private static string? RealPathOf(string full)
    {
        try
        {
            FileSystemInfo? entry =
                File.Exists(full) ? new FileInfo(full)
                : Directory.Exists(full) ? new DirectoryInfo(full)
                : null;

            if (entry is null)
            {
                return null;
            }

            var target = entry.ResolveLinkTarget(returnFinalTarget: true) ?? entry;

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.FullName));
        }
        catch (IOException)
        {
            // A link that points at nothing, or a path the filesystem will not describe. Neither
            // is something this store can read or write, and neither is a way out of the wiki.
            return null;
        }
    }
}

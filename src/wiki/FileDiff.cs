namespace Grimoire.Wiki;

/// <summary>What a commit did to one page.</summary>
public enum ChangeKind
{
    /// <summary>The page did not exist before the commit.</summary>
    Added,

    /// <summary>The page existed and its content changed.</summary>
    Modified,

    /// <summary>The page existed and the commit deleted it.</summary>
    Removed,
}

/// <summary>
/// One page's share of a commit, derived from the commit on read and never stored, so the
/// artifact cannot drift from history (FR-022).
/// </summary>
/// <param name="Path">Wiki-relative page path.</param>
/// <param name="ChangeKind">Added, modified, or removed.</param>
/// <param name="Patch">The content added, changed, and removed.</param>
public sealed record FileDiff(string Path, ChangeKind ChangeKind, string Patch);

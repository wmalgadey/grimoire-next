namespace Grimoire.Wiki;

/// <summary>
/// The single commit produced by a run that changed content — never zero, never more than one
/// (FR-015, SC-002).
/// </summary>
/// <param name="Sha">The wiki commit this run produced. Opaque to the user.</param>
/// <param name="ParentSha">
/// The pre-run tip: the commit a failed run leaves the wiki at (FR-017, SC-003) and the content
/// a revert restores (FR-025).
/// </param>
/// <param name="Message">The run's final assistant message, verbatim (research R9).</param>
/// <param name="CommittedAt">When the commit landed.</param>
public sealed record WikiCommit(
    string Sha,
    string ParentSha,
    string Message,
    DateTimeOffset CommittedAt);

using Grimoire.Wiki;

namespace Grimoire.Tasks;

/// <summary>Which settlement a pending commit belongs to.</summary>
public enum SettlementKind
{
    /// <summary>The one commit a run that changed content produces (FR-015).</summary>
    RunCommit,

    /// <summary>The commit that restores what a run changed (FR-025).</summary>
    Revert,
}

/// <summary>
/// A commit put on record before the wiki branch moves to it, and cleared in the same transaction
/// that records it on its task. Between the two, a hub that dies leaves one of these behind, and
/// the next start finishes the settlement: the commit is either in history, and its task is
/// settled with it, or it is not, and nothing happened (FR-015, FR-025, FR-028).
/// </summary>
/// <param name="TaskId">The task the commit settles.</param>
/// <param name="Kind">Whether it is the task's run commit or its revert.</param>
/// <param name="Commit">The commit exactly as it will be recorded, identity included.</param>
/// <param name="RecordedAt">When it was put on record.</param>
public sealed record PendingSettlement(string TaskId, SettlementKind Kind, WikiCommit Commit, DateTimeOffset RecordedAt);

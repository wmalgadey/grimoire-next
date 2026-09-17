namespace Grimoire.Tasks;

/// <summary>
/// The fact that a task's commit was undone. Creating it sets the task's state to
/// <see cref="TaskState.Reverted"/>. The run's own commit stays in history: the restoration
/// is a new commit, nothing is rewritten or discarded (FR-025).
/// </summary>
/// <param name="RevertCommitSha">The commit that performed the restoration.</param>
/// <param name="RevertedAt">When it landed.</param>
public sealed record RevertRecord(string RevertCommitSha, DateTimeOffset RevertedAt);

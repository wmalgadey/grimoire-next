using Grimoire.Ingest;

namespace Grimoire.Tasks;

/// <summary>
/// Turns an accepted submission into exactly one task (FR-002). The task is openable from the
/// moment it is created, in whatever state it is in (FR-020) — not once a run has succeeded.
/// </summary>
public static class TaskCreation
{
    /// <summary>
    /// Creates the task an accepted submission becomes: one, queued, with its source attached and
    /// a stable identifier that addresses it for the rest of its life.
    /// </summary>
    /// <param name="source">The submitted source, retained as long as the task is (FR-004).</param>
    /// <param name="submittedAt">Orders the queue (FR-019) and the task list (FR-030).</param>
    /// <param name="id">
    /// The identifier. Defaults to a fresh opaque one; passed in only where a caller already has
    /// one, never to let a client choose it.
    /// </param>
    public static Task Create(Source source, DateTimeOffset submittedAt, string? id = null) => new(
        id ?? NewId(),
        TaskState.Queued,
        submittedAt,
        StartedAt: null,
        EndedAt: null,
        source,
        Run: null,
        FailureReason: null,
        Revert: null);

    /// <summary>
    /// An opaque identifier. Sorts by creation time as a string, so the store's cursor stays a
    /// plain tuple comparison, and carries no meaning a user could come to rely on.
    /// </summary>
    private static string NewId() =>
        $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x11}{Guid.NewGuid():N}"[..24];
}

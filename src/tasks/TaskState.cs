namespace Grimoire.Tasks;

/// <summary>
/// The five states a task can be in (FR-020). Exactly these and no others: every state
/// renders on the task view (SC-006), and <see cref="Failed"/> and <see cref="Reverted"/>
/// are terminal.
/// </summary>
public enum TaskState
{
    /// <summary>Accepted, not yet dispatched. Has no run.</summary>
    Queued,

    /// <summary>A run is executing. Has a run with a partial tool-call record and no commit.</summary>
    Running,

    /// <summary>The run ended successfully — with a commit, or with nothing changed (FR-016).</summary>
    Completed,

    /// <summary>
    /// Retrieval failed, or the run failed, aborted, crashed, or hit the run limit. Carries a
    /// human-readable failure reason (FR-018) and no commit (FR-017).
    /// </summary>
    Failed,

    /// <summary>The run's commit was undone by a restoring commit (FR-025).</summary>
    Reverted,
}

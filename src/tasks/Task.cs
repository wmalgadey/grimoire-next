using Grimoire.Ingest;

namespace Grimoire.Tasks;

/// <summary>
/// The user-facing, inspectable record of one ingest — one per accepted submission (FR-002).
/// Openable in every state (FR-020, SC-006).
/// </summary>
/// <param name="Id">Opaque, stable for the task's life; the task view's address (FR-002).</param>
/// <param name="State">One of the five states (FR-020).</param>
/// <param name="SubmittedAt">Orders the queue (FR-019) and the task list, newest first (FR-030).</param>
/// <param name="StartedAt">Set when the run is dispatched.</param>
/// <param name="EndedAt">Set when the run ends, whatever the outcome.</param>
/// <param name="Source">Exactly one, retained as long as the task is (FR-004).</param>
/// <param name="Run">At most one, never more — including across a restart (FR-005).</param>
/// <param name="FailureReason">
/// Human-readable; required whenever the state is <see cref="TaskState.Failed"/> (FR-018).
/// </param>
/// <param name="Revert">Present exactly when the state is <see cref="TaskState.Reverted"/> (FR-025).</param>
public sealed record Task(
    string Id,
    TaskState State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    Source Source,
    AgentRun? Run,
    string? FailureReason,
    RevertRecord? Revert);

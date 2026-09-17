using Grimoire.Wiki;

namespace Grimoire.Tasks;

/// <summary>How a run ended.</summary>
public enum RunOutcomeKind
{
    /// <summary>The agent stopped on its own — with a commit, or having changed nothing.</summary>
    Completed,

    /// <summary>The run failed, aborted, crashed, or hit the run limit. No commit (FR-017).</summary>
    Failed,
}

/// <summary>
/// One execution of the agent for one task. Never more than one per task, including across a
/// restart (FR-005) — enforced by a uniqueness constraint in the store, not by convention.
/// Once the run has ended its instruction version, grant, tool calls, and commit never change
/// (FR-023).
/// </summary>
/// <param name="InstructionVersion">Recorded at dispatch, before the first model call (FR-014).</param>
/// <param name="ToolGrant">Recorded at dispatch, before the first model call (FR-013).</param>
/// <param name="ToolCalls">
/// Append-only during the run, frozen after, ordered by <see cref="ToolCall.Seq"/> (FR-021, FR-023).
/// </param>
/// <param name="Outcome"><c>null</c> while running.</param>
/// <param name="FailureReason">Required when the outcome is <see cref="RunOutcomeKind.Failed"/> (FR-018).</param>
/// <param name="Commit">
/// At most one; <c>null</c> when the run changed nothing (FR-016) or failed (FR-017).
/// </param>
/// <param name="ToolCallCount">Counted against the tool-call half of the run limit (FR-009).</param>
/// <param name="DurationMs">Counted against the elapsed half of the run limit (FR-009).</param>
public sealed record AgentRun(
    InstructionVersion InstructionVersion,
    ToolGrant ToolGrant,
    IReadOnlyList<ToolCall> ToolCalls,
    RunOutcomeKind? Outcome,
    string? FailureReason,
    WikiCommit? Commit,
    int ToolCallCount,
    int? DurationMs)
{
    /// <summary>
    /// A completed run that produced no commit: the agent judged the wiki already said what the
    /// source had to say, and the task view states that explicitly rather than showing an empty
    /// diff (FR-016, SC-011).
    /// </summary>
    public bool ChangedNothing => Outcome is RunOutcomeKind.Completed && Commit is null;
}

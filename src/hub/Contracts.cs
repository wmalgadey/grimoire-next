using Grimoire.Ingest;
using Grimoire.Tasks;
using Grimoire.Wiki;

namespace Grimoire.Hub;

/// <summary>
/// The wire shapes of <c>contracts/hub-api.openapi.yaml</c>.
/// </summary>
/// <remarks>
/// These are the committed contract's shapes, not a second model of the domain: each is projected
/// from the artifact types in one place (<see cref="TaskProjection"/>) and nowhere else, so the
/// wire can change without the domain changing and vice versa, and the drift test catches either
/// getting ahead of the other (constitution V.5, VII.2).
/// </remarks>
public static class Contracts
{
    /// <summary>What the user submitted.</summary>
    public sealed record Submission(string Kind, string Value);

    /// <summary>A task summary, as the task list shows it.</summary>
    public sealed record TaskSummary(
        string Id,
        string State,
        DateTimeOffset SubmittedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EndedAt,
        SourceSummary Source,
        string? FailureReason);

    /// <summary>A page of the task list.</summary>
    public sealed record TaskList(IReadOnlyList<TaskSummary> Tasks, string? NextCursor);

    /// <summary>Everything the task view renders.</summary>
    public sealed record TaskDetail(
        string Id,
        string State,
        DateTimeOffset SubmittedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EndedAt,
        SourceDetail Source,
        string? FailureReason,
        AgentRunDetail? Run,
        RevertRecordDetail? Revert,
        RevertEligibilityDetail RevertEligibility);

    /// <summary>Short identification of a source for a list row; never the whole text.</summary>
    public sealed record SourceSummary(string Kind, string Preview, int ByteLength);

    /// <summary>The source as the task view shows it.</summary>
    public sealed record SourceDetail(
        string Kind,
        string SubmittedValue,
        string? RetrievedText,
        DateTimeOffset? RetrievedAt,
        int ByteLength);

    /// <summary>The run as the task view shows it.</summary>
    public sealed record AgentRunDetail(
        InstructionVersionDetail InstructionVersion,
        ToolGrantDetail ToolGrant,
        IReadOnlyList<ToolCallDetail> ToolCalls,
        string? Outcome,
        string? FailureReason,
        int ToolCallCount,
        int? DurationMs,
        bool ChangedNothing,
        WikiCommitDetail? Commit);

    /// <summary>Which instruction revision the run's behaviour is attributable to.</summary>
    public sealed record InstructionVersionDetail(string Path, string Sha256, int ByteLength);

    /// <summary>The set of actions the agent was allowed.</summary>
    public sealed record ToolGrantDetail(IReadOnlyList<string> Tools, DateTimeOffset RecordedAt);

    /// <summary>One call the agent made, refusals included.</summary>
    public sealed record ToolCallDetail(
        int Seq,
        string Tool,
        string? Target,
        string Outcome,
        string? Detail,
        DateTimeOffset At);

    /// <summary>The run's one commit and the diff read back from history.</summary>
    public sealed record WikiCommitDetail(
        string Sha,
        string ParentSha,
        string Message,
        DateTimeOffset CommittedAt,
        IReadOnlyList<FileDiffDetail> FileDiffs);

    /// <summary>One page's share of the commit.</summary>
    public sealed record FileDiffDetail(string Path, string ChangeKind, string Patch);

    /// <summary>The fact that a task's commit was undone.</summary>
    public sealed record RevertRecordDetail(string RevertCommitSha, DateTimeOffset RevertedAt);

    /// <summary>
    /// What the task view renders where the revert action would be. The reason is the point:
    /// <c>superseded</c> is explained in words rather than shown as a disabled control (FR-027).
    /// </summary>
    public sealed record RevertEligibilityDetail(bool Eligible, string? Reason);
}

/// <summary>
/// Projects artifact types onto the committed contract's shapes. The one place that translation
/// happens, so there is one representation of each concept and one place a wire change appears
/// (constitution VII.2).
/// </summary>
public static class TaskProjection
{
    /// <summary>The longest preview a list row carries. Identification, not content.</summary>
    private const int PreviewLength = 120;

    /// <summary>Projects a task onto its list row.</summary>
    public static Contracts.TaskSummary ToSummary(Grimoire.Tasks.Task task) => new(
        task.Id,
        StateName(task.State),
        task.SubmittedAt,
        task.StartedAt,
        task.EndedAt,
        new Contracts.SourceSummary(SourceKindName(task.Source.Kind), Preview(task.Source), task.Source.ByteLength),
        task.FailureReason);

    /// <summary>Projects a task onto the task view, with its revert eligibility.</summary>
    public static Contracts.TaskDetail ToDetail(
        Grimoire.Tasks.Task task,
        IReadOnlyList<FileDiff> fileDiffs,
        Contracts.RevertEligibilityDetail eligibility) => new(
        task.Id,
        StateName(task.State),
        task.SubmittedAt,
        task.StartedAt,
        task.EndedAt,
        new Contracts.SourceDetail(
            SourceKindName(task.Source.Kind),
            task.Source.SubmittedValue,
            task.Source.RetrievedText,
            task.Source.RetrievedAt,
            task.Source.ByteLength),
        task.FailureReason,
        task.Run is null ? null : ToRunDetail(task.Run, fileDiffs),
        task.Revert is null ? null : new Contracts.RevertRecordDetail(task.Revert.RevertCommitSha, task.Revert.RevertedAt),
        eligibility);

    private static Contracts.AgentRunDetail ToRunDetail(AgentRun run, IReadOnlyList<FileDiff> fileDiffs) => new(
        new Contracts.InstructionVersionDetail(
            run.InstructionVersion.Path, run.InstructionVersion.Sha256, run.InstructionVersion.ByteLength),
        new Contracts.ToolGrantDetail(run.ToolGrant.Tools, run.ToolGrant.RecordedAt),
        [.. run.ToolCalls.Select(call => new Contracts.ToolCallDetail(
            call.Seq, call.Tool, call.Target, OutcomeName(call.Outcome), call.Detail, call.At))],
        run.Outcome switch
        {
            RunOutcomeKind.Completed => "completed",
            RunOutcomeKind.Failed => "failed",
            _ => null,
        },
        run.FailureReason,
        run.ToolCallCount,
        run.DurationMs,
        run.ChangedNothing,
        run.Commit is null ? null : new Contracts.WikiCommitDetail(
            run.Commit.Sha,
            run.Commit.ParentSha,
            run.Commit.Message,
            run.Commit.CommittedAt,
            [.. fileDiffs.Select(diff => new Contracts.FileDiffDetail(
                diff.Path, ChangeKindName(diff.ChangeKind), diff.Patch))]));

    /// <summary>Enough of the source to recognise the row by, and no more.</summary>
    private static string Preview(Source source)
    {
        var value = source.SubmittedValue.Trim().ReplaceLineEndings(" ");
        return value.Length <= PreviewLength ? value : value[..PreviewLength] + "…";
    }

    private static string StateName(TaskState state) => state switch
    {
        TaskState.Queued => "queued",
        TaskState.Running => "running",
        TaskState.Completed => "completed",
        TaskState.Failed => "failed",
        TaskState.Reverted => "reverted",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static string SourceKindName(SourceKind kind) => kind is SourceKind.Text ? "text" : "url";

    private static string OutcomeName(ToolCallOutcome outcome) => outcome switch
    {
        ToolCallOutcome.Ok => "ok",
        ToolCallOutcome.Failed => "failed",
        ToolCallOutcome.Refused => "refused",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static string ChangeKindName(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Modified => "modified",
        ChangeKind.Removed => "removed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

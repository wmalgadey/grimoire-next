using Grimoire.Wiki;

namespace Grimoire.Dispatch;

/// <summary>
/// What a run ended as, before the hub has done anything about it. Exactly three cases, because
/// there are exactly three things the hub can do next: commit, record a no-change, or reset.
/// </summary>
/// <remarks>
/// Written as a single value with three named handlers from the start (plan VII.3). The run-end
/// path is the one method in this system that would otherwise accumulate conditionals — commit or
/// not, failed or not, limit or crash — until nobody could tell which combinations were reachable.
/// </remarks>
public abstract record RunOutcome
{
    private RunOutcome()
    {
    }

    /// <summary>The agent stopped on its own and the working tree differs from the tip.</summary>
    /// <param name="CommitMessage">The agent's final message, verbatim (research R9).</param>
    /// <param name="ToolCallCount">Calls made, refusals included.</param>
    public sealed record Changed(string? CommitMessage, int ToolCallCount) : RunOutcome;

    /// <summary>
    /// The agent stopped on its own having changed nothing — a real answer, and a good one when
    /// the wiki already said what the source had to say (FR-016, SC-011).
    /// </summary>
    public sealed record ChangedNothing(int ToolCallCount) : RunOutcome;

    /// <summary>
    /// The run failed, crashed, was aborted, or hit its limit. All four are the same thing to the
    /// hub: no commit, and the working tree reset (FR-009, FR-017).
    /// </summary>
    /// <param name="Reason">Human-readable, and required (FR-018).</param>
    public sealed record Failed(string Reason, int ToolCallCount) : RunOutcome;
}

/// <summary>What the hub did about a run's outcome.</summary>
/// <param name="Commit">The one commit, when there was one.</param>
/// <param name="FailureReason">The reason, when the run failed.</param>
/// <param name="ToolCallCount">Calls made, refusals included.</param>
public sealed record RunSettlement(WikiCommit? Commit, string? FailureReason, int ToolCallCount)
{
    /// <summary>Whether the task ends completed or failed.</summary>
    public bool Succeeded => FailureReason is null;
}

/// <summary>The three things the hub does at run end, one per outcome.</summary>
public sealed class RunOutcomeHandler(WikiMutation wiki)
{
    /// <summary>Settles a run: commits, records a no-change, or resets.</summary>
    public Task<RunSettlement> Settle(string taskId, RunOutcome outcome, CancellationToken cancellationToken) =>
        outcome switch
        {
            RunOutcome.Changed changed => Commit(taskId, changed, cancellationToken),
            RunOutcome.ChangedNothing unchanged => NoChange(unchanged, cancellationToken),
            RunOutcome.Failed failed => Reset(failed, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };

    private async Task<RunSettlement> Commit(string taskId, RunOutcome.Changed changed, CancellationToken cancellationToken)
    {
        // Commits, and leaves the working tree equal to the new tip — a file the run wrote that the
        // wiki ignores is in no commit, so it does not survive the run (FR-015, FR-017).
        var commit = await wiki.CommitRun(taskId, changed.CommitMessage, cancellationToken);
        return new RunSettlement(commit, null, changed.ToolCallCount);
    }

    private async Task<RunSettlement> NoChange(RunOutcome.ChangedNothing unchanged, CancellationToken cancellationToken)
    {
        // A completed run that wrote nothing can still have left something behind — a page
        // rewritten with identical bytes leaves the tree clean, but a stray temp file would not.
        // Resetting keeps "no commit" and "no leftovers" the same statement.
        await wiki.ResetWorkingTree(cancellationToken);
        return new RunSettlement(null, null, unchanged.ToolCallCount);
    }

    private async Task<RunSettlement> Reset(RunOutcome.Failed failed, CancellationToken cancellationToken)
    {
        await wiki.ResetWorkingTree(cancellationToken);
        return new RunSettlement(null, failed.Reason, failed.ToolCallCount);
    }
}

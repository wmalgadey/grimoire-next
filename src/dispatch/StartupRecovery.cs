using Grimoire.Tasks;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Grimoire.Dispatch;

/// <summary>
/// What a start does about the previous one (FR-028, contracts/deployment.md "Startup", step 3).
/// </summary>
/// <remarks>
/// A hub that is killed — an OOM kill, a node drain, a `kill -9` — leaves the state database
/// saying a run is executing when no process is. Nothing else ever revisits that task, so without
/// this it stays <c>running</c> for good: the one state an operator can neither wait out nor act
/// on.
///
/// Recovery is deliberately blunt about runs. It does not try to work out how far a run got or to
/// resume it: its output was never committed, the working tree is reset, and a second attempt at
/// the same task would break "at most one run per task, ever" (FR-005). Every interrupted task is
/// failed with a reason that names the interruption, and the wiki is put back to its last commit.
///
/// The one thing it finishes is a settlement: a commit the previous process put on record and may
/// have moved the branch to before it died. That run, or that revert, is done — only its record is
/// missing — so the commit is recorded on its task if it is in history, and forgotten if not.
///
/// It is also the backstop for the graceful path. <see cref="GracefulShutdown"/> reaches the same
/// end state on <c>SIGTERM</c>; when the process never got the chance, this does it on the way up.
/// </remarks>
public sealed class StartupRecovery(
    SqliteStore store,
    WikiMutation wiki,
    RunQueue queue,
    ILogger<StartupRecovery> logger)
{
    /// <summary>The reason an interrupted task carries, in the operator's words (FR-018).</summary>
    public const string InterruptedReason =
        "The hub was interrupted while this run was executing. Nothing was committed, and the run "
        + "is not retried: submit the source again to start a new one.";

    /// <summary>
    /// Fails whatever was left running, resets the working tree, then dispatches the backlog —
    /// in that order, because a queued task must not be dispatched into a dirty tree left by the
    /// run that died.
    /// </summary>
    public async Task Recover(CancellationToken cancellationToken)
    {
        // First, the settlements the previous process began: a commit it put on record and may
        // have moved the branch to before it died. Settled before anything is failed, because a
        // task whose commit is in history finished its run — only its record did not.
        foreach (var pending in store.ListPendingSettlements())
        {
            Settle(pending);
        }

        var interrupted = store.ListTasksInState(TaskState.Running);
        foreach (var task in interrupted)
        {
            store.FailTask(task.Id, InterruptedReason, DateTimeOffset.UtcNow);
            // The run ended when the previous process did; this is the first process able to say so.
            logger.LogInformation(
                "grimoire.run.ended {TaskId} {Outcome} {FailureReason} {ToolCallCount} {DurationMs}",
                task.Id, "failed", InterruptedReason, task.Run?.ToolCalls.Count ?? 0, null);
            logger.LogInformation(
                "grimoire.task.state_changed {TaskId} {State} {FailureReason}",
                task.Id, "failed", InterruptedReason);
        }

        // Anything the dead run wrote is discarded, so the wiki is byte-identical to the commit it
        // was at before that run started (FR-017).
        await wiki.ResetWorkingTree(cancellationToken);

        var queued = store.ListTasksInState(TaskState.Queued);
        logger.LogInformation(
            "grimoire.dispatch.recovered_on_startup {FailedTaskIds} {RequeuedTaskIds}",
            interrupted.Select(task => task.Id).ToArray(),
            queued.Select(task => task.Id).ToArray());

        queue.EnqueueBacklog();
    }

    /// <summary>
    /// Finishes a settlement the previous process began. A commit in history is recorded on its
    /// task, exactly as that process would have; one that never reached history is forgotten, and
    /// the task is handled as if the commit had never been built (FR-015, FR-025, FR-028).
    /// </summary>
    private void Settle(PendingSettlement pending)
    {
        if (!wiki.IsInHistory(pending.Commit.Sha))
        {
            store.DiscardPendingSettlement(pending.TaskId);
            return;
        }

        var task = store.GetTask(pending.TaskId);
        var settled = pending.Kind switch
        {
            SettlementKind.RunCommit => store.EndRun(
                pending.TaskId, RunOutcomeKind.Completed, null, pending.Commit,
                task?.StartedAt is { } startedAt ? (int)(pending.RecordedAt - startedAt).TotalMilliseconds : null,
                pending.RecordedAt),
            _ => store.RecordRevert(pending.TaskId, new RevertRecord(pending.Commit.Sha, pending.Commit.CommittedAt)),
        };

        if (!settled)
        {
            // The task had moved on without it. The commit stands in history regardless, and
            // nothing here can say what it should mean now.
            store.DiscardPendingSettlement(pending.TaskId);
            logger.LogCritical(
                "The commit {CommitSha} for task {TaskId} is in the wiki, but the task was no longer in a state "
                + "to record it when the hub restarted. An operator needs to reconcile the task with wiki history.",
                pending.Commit.Sha, pending.TaskId);
            return;
        }

        if (pending.Kind is SettlementKind.RunCommit)
        {
            logger.LogInformation(
                "grimoire.wiki.committed {TaskId} {CommitSha} {FilesChanged}",
                pending.TaskId, pending.Commit.Sha, wiki.DiffOf(pending.Commit.Sha).Count);
            logger.LogInformation(
                "grimoire.run.ended {TaskId} {Outcome} {FailureReason} {ToolCallCount} {DurationMs}",
                pending.TaskId, "completed", null, task?.Run?.ToolCalls.Count ?? 0, null);
            logger.LogInformation("grimoire.task.state_changed {TaskId} {State}", pending.TaskId, "completed");
        }
        else
        {
            logger.LogInformation(
                "grimoire.wiki.reverted {TaskId} {RevertCommitSha}", pending.TaskId, pending.Commit.Sha);
            logger.LogInformation("grimoire.task.state_changed {TaskId} {State}", pending.TaskId, "reverted");
        }
    }
}

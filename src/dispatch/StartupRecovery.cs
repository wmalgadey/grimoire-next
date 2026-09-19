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
/// Git history is the one journal a commit is ever written to. Before anything is failed,
/// <see cref="ReconcileHead"/> reads HEAD and, if it is a commit no task recorded — the previous
/// process died between moving the branch and writing the store — attributes it to whichever task
/// it plainly belongs to. There is no second, provisional record of a commit kept anywhere else.
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
    /// Reconciles HEAD against the task store, fails whatever was left running, resets the working
    /// tree, then dispatches the backlog — in that order, because a queued task must not be
    /// dispatched into a dirty tree left by the run that died.
    /// </summary>
    public async Task Recover(CancellationToken cancellationToken)
    {
        ReconcileHead();

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
    /// Attributes a HEAD the previous process moved the branch to but died before recording
    /// (FR-015, FR-025, FR-028). Run before the running→failed pass, because a HEAD that turns out
    /// to be that pass's own task's run commit must be adopted as completed, not failed.
    /// </summary>
    private void ReconcileHead()
    {
        var head = wiki.Tip();
        if (store.FindTaskByCommit(head) is not null)
        {
            // Already accounted for — the ordinary case after a clean run or a clean revert.
            return;
        }

        var commit = wiki.CommitAt(head);
        if (commit.ParentSha.Length is 0)
        {
            // HEAD has no parent, so it is the wiki repository's own root commit — the one every
            // run and every revert is built on top of (constitution assumption: the wiki has at
            // least one commit before the first ingest). It predates every task and is never a
            // commit to attribute to one, whether none has run yet or one is running but has not
            // written anything.
            return;
        }

        // (a) A revert: the hub made HEAD as one, and its parent is the commit some task's run
        // recorded. Told by the author identity the hub gives a revert, never by the message — a
        // run's message is the model's text and may well begin "Revert".
        if (wiki.IsRevert(head))
        {
            if (store.FindTaskByRunCommit(commit.ParentSha) is { } revertedTask
                && store.RecordRevert(revertedTask.Id, new RevertRecord(head, commit.CommittedAt)))
            {
                logger.LogInformation("grimoire.wiki.reverted {TaskId} {RevertCommitSha}", revertedTask.Id, head);
                logger.LogInformation("grimoire.task.state_changed {TaskId} {State}", revertedTask.Id, "reverted");
                return;
            }

            // A revert of a commit no completed task holds. Nothing here can say which undo this
            // was, so nothing is claimed; it is never a run commit either, whatever is running.
            logger.LogCritical(
                "HEAD {Sha} is a revert no task recorded, of {ParentSha}, which no completed task's run "
                + "made. An operator needs to reconcile it with the task store by hand.",
                head, commit.ParentSha);
            return;
        }

        // (b) A run commit: exactly one task was left running, so it can only be that run's.
        var running = store.ListTasksInState(TaskState.Running);
        if (running.Count is 1)
        {
            var task = running[0];
            var durationMs = task.StartedAt is { } startedAt
                ? (int)(commit.CommittedAt - startedAt).TotalMilliseconds
                : (int?)null;
            store.EndRun(task.Id, RunOutcomeKind.Completed, null, commit, durationMs, commit.CommittedAt);
            logger.LogInformation(
                "grimoire.wiki.committed {TaskId} {CommitSha} {FilesChanged}",
                task.Id, head, wiki.DiffOf(head).Count);
            logger.LogInformation(
                "grimoire.run.ended {TaskId} {Outcome} {FailureReason} {ToolCallCount} {DurationMs}",
                task.Id, "completed", null, task.Run?.ToolCalls.Count ?? 0, durationMs);
            logger.LogInformation("grimoire.task.state_changed {TaskId} {State}", task.Id, "completed");
            return;
        }

        // (c) Neither shape fits. Left to the running→failed pass; an operator has to reconcile
        // this HEAD with the task store by hand.
        logger.LogCritical(
            "HEAD {Sha} is a commit no task recorded, and it could not be attributed: {RunningCount} tasks "
            + "were left running, not exactly one. An operator needs to reconcile it by hand.",
            head, running.Count);
    }
}

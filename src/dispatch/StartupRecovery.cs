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
/// Recovery is deliberately blunt. It does not try to work out how far the run got or to resume
/// it: the run's output was never committed, the working tree is reset, and a second attempt at
/// the same task would break "at most one run per task, ever" (FR-005). Every interrupted task is
/// failed with a reason that names the interruption, and the wiki is put back to its last commit.
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
}

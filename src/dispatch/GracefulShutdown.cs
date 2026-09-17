using Grimoire.Tasks;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Grimoire.Dispatch;

/// <summary>
/// What <c>SIGTERM</c> does (FR-017, FR-028, contracts/deployment.md "SIGTERM").
/// </summary>
/// <remarks>
/// The steps are in the contract's order, and the order is the point. Terminating the runner
/// before failing its task means the task is not marked failed while a runner is still writing
/// into the tree; resetting last means the wiki ends at the pre-run commit whatever the runner had
/// managed to write.
///
/// Step 1 — "/readyz reports not-ready so traffic drains" — is not here. Readiness is the hub
/// slice's surface and this is the dispatch slice; a dispatch type reaching into the hub would
/// invert the dependency. The composition root marks the replica draining immediately before
/// calling this, which is the one place that sequencing can live without either slice knowing the
/// other.
///
/// A graceful stop and a kill converge on the same end state — this reaches it now,
/// <see cref="StartupRecovery"/> reaches it on the next start. That is why neither is allowed to
/// be the only one: an orchestrator that loses the signal must not produce a different outcome
/// from one that delivers it.
///
/// Queued tasks are deliberately left queued. They have not run, nothing about them is in doubt,
/// and the next start dispatches them in submission order.
/// </remarks>
public sealed class GracefulShutdown(
    SqliteStore store,
    WikiMutation wiki,
    RunQueue queue,
    ILogger<GracefulShutdown> logger)
{
    /// <summary>The reason an interrupted task carries (FR-018).</summary>
    public const string InterruptedReason =
        "The hub was shut down while this run was executing. Nothing was committed, and the run is "
        + "not retried: submit the source again to start a new one.";

    /// <summary>How long the runner is given to die before its task is settled anyway.</summary>
    private static readonly TimeSpan RunnerGrace = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the shutdown sequence, from step 2 onwards. Safe to call when no run is executing.
    /// </summary>
    public async Task Drain(CancellationToken cancellationToken)
    {
        // 2. Stop accepting dispatches and cancel the running one, which kills the runner process.
        queue.StopAccepting();

        var deadline = DateTime.UtcNow + RunnerGrace;
        while (queue.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, CancellationToken.None);
        }

        // 3. Whatever was executing is failed with a reason naming the interruption. Read from the
        // store rather than remembered in a field: the dispatcher may already have settled the run
        // on its own, and then there is nothing here to do.
        foreach (var task in store.ListTasksInState(TaskState.Running))
        {
            store.FailTask(task.Id, InterruptedReason, DateTimeOffset.UtcNow);
            logger.LogInformation("grimoire.dispatch.interrupted_on_shutdown {TaskId}", task.Id);
            logger.LogInformation(
                "grimoire.task.state_changed {TaskId} {State} {FailureReason}",
                task.Id, "failed", InterruptedReason);
        }

        // 4. Nothing was committed, so resetting leaves the wiki at the pre-run commit (FR-017).
        await wiki.ResetWorkingTree(CancellationToken.None);
    }
}

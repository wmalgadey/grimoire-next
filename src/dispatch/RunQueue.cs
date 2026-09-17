using Grimoire.Tasks.Adapters;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Grimoire.Dispatch;

/// <summary>
/// One run at a time, dispatched in <c>submittedAt</c> order (FR-019).
/// </summary>
/// <remarks>
/// Serialisation is a property of the product, not a limitation awaiting a fix: runs hold the wiki
/// working tree, so two at once would interleave their writes into one commit. That is also why
/// the deployment is one replica (contracts/deployment.md).
/// </remarks>
public sealed class RunQueue(SqliteStore store, Dispatcher dispatcher, ILogger<RunQueue> logger) : IDisposable
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private volatile bool _accepting = true;

    /// <summary>
    /// Queues a task for dispatch. Returns immediately: submitting is not waiting for a run.
    /// </summary>
    public void Enqueue(string taskId)
    {
        if (!_accepting)
        {
            return;
        }

        _ = Task.Run(() => DispatchWhenFree(taskId), CancellationToken.None);
    }

    /// <summary>
    /// Dispatches every queued task in submission order. Used at startup, after recovery has
    /// failed whatever was left running (contracts/deployment.md "Startup", step 4).
    /// </summary>
    public void EnqueueBacklog()
    {
        foreach (var task in store.ListTasksInState(Grimoire.Tasks.TaskState.Queued))
        {
            Enqueue(task.Id);
        }
    }

    /// <summary>
    /// Stops accepting dispatches and cancels the running one, so traffic drains and the run's
    /// working tree is reset rather than half-committed (contracts/deployment.md "SIGTERM").
    /// </summary>
    public void StopAccepting()
    {
        _accepting = false;
        _stopping.Cancel();
    }

    /// <summary>Whether a run is executing right now.</summary>
    public bool IsRunning => _oneAtATime.CurrentCount is 0;

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
        _oneAtATime.Dispose();
    }

    private async Task DispatchWhenFree(string taskId)
    {
        try
        {
            await _oneAtATime.WaitAsync(_stopping.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down. The task stays queued and the next start dispatches it.
            return;
        }

        try
        {
            var task = store.GetTask(taskId);
            if (task is null || task.State is not Grimoire.Tasks.TaskState.Queued)
            {
                // Already run, already failed, or gone. Never a second run (FR-005).
                return;
            }

            await dispatcher.Dispatch(task, _stopping.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "grimoire.run.ended {TaskId} {Outcome}", taskId, "failed");
        }
        finally
        {
            _oneAtATime.Release();
        }
    }
}

using System.Threading.Channels;
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
///
/// FIFO is kept by a single background consumer draining one channel, not by a semaphore guarding
/// a fire-and-forget <c>Task.Run</c> per submission: two enqueues racing to even start waiting on a
/// semaphore is not the same guarantee as the order they were enqueued in, and the .NET thread
/// pool does not promise one.
/// </remarks>
public sealed class RunQueue : IDisposable
{
    private readonly SqliteStore store;
    private readonly Dispatcher dispatcher;
    private readonly ILogger<RunQueue> logger;
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource stopping = new();
    private readonly Task consumer;
    private volatile bool dispatching;

    public RunQueue(SqliteStore store, Dispatcher dispatcher, ILogger<RunQueue> logger)
    {
        this.store = store;
        this.dispatcher = dispatcher;
        this.logger = logger;
        consumer = Task.Run(() => Consume(stopping.Token), CancellationToken.None);
    }

    /// <summary>
    /// Queues a task for dispatch. Returns immediately: submitting is not waiting for a run.
    /// </summary>
    public void Enqueue(string taskId) => queue.Writer.TryWrite(taskId);

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
        queue.Writer.TryComplete();
        stopping.Cancel();
    }

    /// <summary>Whether a run is executing right now.</summary>
    public bool IsRunning => dispatching;

    /// <inheritdoc />
    public void Dispose()
    {
        stopping.Cancel();
        queue.Writer.TryComplete();
        stopping.Dispose();
    }

    /// <summary>
    /// The one consumer: reads tasks strictly in the order they were written to the channel — the
    /// order <see cref="Enqueue"/> was called in, which is <c>submittedAt</c> order both for a live
    /// submission and for <see cref="EnqueueBacklog"/> — and dispatches them one at a time.
    /// </summary>
    private async Task Consume(CancellationToken cancellationToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(cancellationToken))
            {
                while (queue.Reader.TryRead(out var taskId))
                {
                    await DispatchOne(taskId, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task DispatchOne(string taskId, CancellationToken cancellationToken)
    {
        dispatching = true;
        try
        {
            var task = store.GetTask(taskId);
            if (task is null || task.State is not Grimoire.Tasks.TaskState.Queued)
            {
                // Already run, already failed, or gone. Never a second run (FR-005).
                return;
            }

            await dispatcher.Dispatch(task, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down. The task stays queued and the next start dispatches it.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "grimoire.run.ended {TaskId} {Outcome}", taskId, "failed");

            // Dispatcher.Dispatch settles every outcome it reaches internally; this catches
            // whatever happens before it gets that far (constructing the runner, resolving
            // configuration). Without this, an exception here — not one Dispatch already turned
            // into a failed run — would leave the task 'queued' forever: nothing else ever
            // revisits it.
            try
            {
                if (store.GetTask(taskId) is { State: Grimoire.Tasks.TaskState.Queued })
                {
                    store.FailTask(
                        taskId,
                        $"The run could not be started: {exception.Message}",
                        DateTimeOffset.UtcNow);
                }
            }
            catch (Exception storeException)
            {
                logger.LogError(storeException, "grimoire.run.ended {TaskId} {Outcome}", taskId, "failed");
            }
        }
        finally
        {
            dispatching = false;
        }
    }
}

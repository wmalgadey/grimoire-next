using System.Text;
using Grimoire.Dispatch.Adapters;
using Grimoire.Ingest;
using Grimoire.Ingest.Adapters;
using Grimoire.Tasks;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki;
using Microsoft.Extensions.Logging;

// The artifact is called Task, because that is what the product calls it. This file is also
// async throughout, so the bare name is the async type here and the artifact is named in full.
using Task = System.Threading.Tasks.Task;

namespace Grimoire.Dispatch;

/// <summary>Everything the dispatcher needs that comes from configuration.</summary>
/// <param name="RepositoryRoot">The repository the runner entry point is resolved against.</param>
/// <param name="WikiRepositoryPath">The working tree pinned as the runner's <c>cwd</c>.</param>
/// <param name="ModelBaseUrl">The proxy's model route.</param>
/// <param name="ModelToken">An opaque internal token for the proxy (ADR-0010).</param>
/// <param name="InstructionPath">The instruction file the runner loads.</param>
/// <param name="Limit">The run's two ceilings (FR-009).</param>
public sealed record DispatchSettings(
    string RepositoryRoot,
    string WikiRepositoryPath,
    string ModelBaseUrl,
    string ModelToken,
    string InstructionPath,
    RunLimit Limit);

/// <summary>
/// Dispatches exactly one run per task whose source is available (FR-005), and is the only place
/// that decides what a run's events mean for the artifact.
/// </summary>
public sealed class Dispatcher(
    SqliteStore store,
    WikiMutation wiki,
    RunOutcomeHandler outcomes,
    UrlFetch urlFetch,
    DispatchSettings settings,
    ILogger<Dispatcher> logger)
{
    /// <summary>
    /// The grant the hub configured. What the runner reports is asserted against this, and a
    /// mismatch fails the run rather than being logged and ignored
    /// (contracts/runner-protocol.md, "tool_grant").
    /// </summary>
    public static readonly string[] ConfiguredGrant =
        ["mcp__wiki__read_page", "mcp__wiki__write_page"];

    /// <summary>Runs one task's one run, from spawn to settled artifact.</summary>
    public async Task Dispatch(Grimoire.Tasks.Task task, CancellationToken cancellationToken)
    {
        var sourceText = task.Source.TextForRun ?? await Retrieve(task, cancellationToken);
        if (sourceText is null)
        {
            // Retrieval failed and the task says why. No run is dispatched (FR-003).
            return;
        }

        // "Is the run failing on its own account, or because the egress path is broken?" is a
        // question an operator has to be able to answer, and they cannot answer it from a run that
        // sat there until its elapsed ceiling. The SDK retries a connection failure with backoff,
        // so without this probe an unreachable endpoint is a hang rather than a reason (plan IV,
        // grimoire.run.model_endpoint_unreachable; TS-18).
        if (ModelEndpointProbe.Unreachable(settings.ModelBaseUrl, TimeSpan.FromSeconds(5)) is { } probe)
        {
            var unreachable = $"{probe} No run was dispatched: this is the egress path, not the agent.";
            logger.LogWarning(
                "grimoire.run.model_endpoint_unreachable {TaskId} {Endpoint} {Status}",
                task.Id, settings.ModelBaseUrl, "unreachable");
            store.FailTask(task.Id, unreachable, DateTimeOffset.UtcNow);
            logger.LogInformation("grimoire.task.state_changed {TaskId} {State}", task.Id, "failed");
            return;
        }

        // The run starts from the tip, whatever the tree held before it. Every path that ends a
        // run resets it already, but a reset that itself failed, or a write nothing accounts for,
        // would otherwise reach this agent and ride along in its commit (FR-015, FR-017).
        await wiki.ResetWorkingTree(cancellationToken);

        var startedAt = DateTimeOffset.UtcNow;
        var runner = new RunnerProcess(
            settings.WikiRepositoryPath,
            home => RunnerEnvironment.ForRun(
                task.Id,
                settings.ModelBaseUrl,
                settings.ModelToken,
                settings.InstructionPath,
                home,
                settings.RepositoryRoot));

        InstructionLoadedEvent? instruction = null;
        ToolGrantEvent? grant = null;
        string? grantMismatch = null;
        var toolCallCount = 0;

        try
        {
            var exit = await runner.Run(
                new DispatchMessage(task.Id, sourceText, settings.Limit.MaxToolCalls, settings.Limit.MaxElapsedMs),
                settings.Limit,
                onEvent: async runnerEvent =>
                {
                    switch (runnerEvent)
                    {
                        case InstructionLoadedEvent loaded:
                            instruction = loaded;
                            break;

                        case ToolGrantEvent reported:
                            grant = reported;
                            grantMismatch = GrantMismatch(reported);
                            break;

                        case ToolCallEvent call:
                            toolCallCount++;
                            store.AppendToolCall(task.Id, call.ToToolCall());
                            logger.LogInformation(
                                "grimoire.run.tool_call {TaskId} {Seq} {Tool} {Target} {Outcome}",
                                task.Id, call.Seq, call.Tool, call.Target, call.Outcome);
                            break;
                    }

                    await Task.CompletedTask;
                },
                gate: async () =>
                {
                    // The gate. Both facts are durably persisted on the task before `proceed` is
                    // sent, which is what makes "recorded before the first model call" a property
                    // a test can assert rather than a hope about scheduling (FR-013, FR-014).
                    if (instruction is null || grant is null)
                    {
                        throw new RunnerProtocolException(
                            "The runner asked to proceed before reporting its instruction version and grant.");
                    }

                    // Refuse here, before the gate opens and `proceed` is ever sent: this is the
                    // last point at which the model has not yet been invoked. Catching the
                    // mismatch only in Interpret() after the run has already executed would let a
                    // runner that reports a wider grant reach the model and use those tools before
                    // the hub notices — a reset undoes the working tree, not whatever else those
                    // tools touched (deny-by-default, constitution II).
                    if (grantMismatch is not null)
                    {
                        throw new RunnerProtocolException(grantMismatch);
                    }

                    store.StartRun(
                        task.Id,
                        new AgentRun(
                            instruction.ToInstructionVersion(),
                            new ToolGrant(grant.Tools, DateTimeOffset.UtcNow),
                            [],
                            null,
                            null,
                            null,
                            0,
                            null),
                        startedAt);

                    logger.LogInformation(
                        "grimoire.run.dispatched {TaskId} {InstructionVersion} {ToolGrant}",
                        task.Id, instruction.Sha256, string.Join(',', grant.Tools));
                    logger.LogInformation(
                        "grimoire.task.state_changed {TaskId} {State}", task.Id, "running");

                    await Task.CompletedTask;
                },
                cancellationToken);

            if (exit.RunEnd?.ModelEndpointStatus is { } status)
            {
                // The path exists and answered with an error — the case a TCP probe cannot see
                // (plan IV).
                logger.LogWarning(
                    "grimoire.run.model_endpoint_unreachable {TaskId} {Endpoint} {Status}",
                    task.Id, settings.ModelBaseUrl, status);
            }

            if (exit.RunEnd is null && exit.ExitCode is not 0)
            {
                // Diagnostics, not task state: the reason on the task says where to look.
                logger.LogWarning(
                    "The runner for task {TaskId} exited with code {ExitCode}. Its stderr: {Diagnostics}",
                    task.Id, exit.ExitCode, exit.Diagnostics);
            }

            var treeChanged = await wiki.HasUncommittedChanges(cancellationToken);
            var outcome = Interpret(exit, grantMismatch, toolCallCount, treeChanged, cancellationToken);
            await Settle(task.Id, outcome, startedAt, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The run for task {TaskId} could not be completed.", task.Id);
            await Settle(
                task.Id,
                new RunOutcome.Failed($"The run could not be completed: {exception.Message}", toolCallCount),
                startedAt,
                cancellationToken);
        }
    }

    /// <summary>
    /// Retrieves a URL task's source, as the first step of its dispatch — so the submission is
    /// answered at once (SC-001) and the task keeps its place in submission order (FR-019). A task
    /// whose retrieval a stopped hub interrupted is still queued with no text, and is retrieved on
    /// the next start rather than stranded.
    /// </summary>
    /// <returns>The retrieved text, or <c>null</c> when retrieval failed and the task was failed.</returns>
    private async Task<string?> Retrieve(Grimoire.Tasks.Task task, CancellationToken cancellationToken)
    {
        string failure;
        try
        {
            var result = await urlFetch.Retrieve(task.Source.SubmittedValue, cancellationToken);
            if (result.Succeeded)
            {
                store.AttachRetrievedText(
                    task.Id, result.Text!, DateTimeOffset.UtcNow, Encoding.UTF8.GetByteCount(result.Text!));
                return result.Text;
            }

            failure = result.Failure!;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Every way retrieval can go wrong ends the task with a reason; none leaves it queued
            // with nothing to run (FR-003, SC-006).
            failure = $"{task.Source.SubmittedValue} could not be retrieved: {exception.Message}";
        }

        if (store.FailTask(task.Id, failure, DateTimeOffset.UtcNow))
        {
            logger.LogInformation(
                "grimoire.task.state_changed {TaskId} {State} {FailureReason}", task.Id, "failed", failure);
        }

        return null;
    }

    /// <summary>What a runner exit means for the artifact.</summary>
    /// <param name="treeChanged">Whether the working tree differs from the tip, ignored files aside.</param>
    private RunOutcome Interpret(
        RunnerExit exit,
        string? grantMismatch,
        int toolCallCount,
        bool treeChanged,
        CancellationToken cancellationToken)
    {
        if (grantMismatch is not null)
        {
            return new RunOutcome.Failed(grantMismatch, toolCallCount);
        }

        if (settings.Limit.ToolCallsExceeded(toolCallCount))
        {
            return new RunOutcome.Failed(settings.Limit.ToolCallReason(toolCallCount), toolCallCount);
        }

        if (exit.RunEnd is null)
        {
            // No run_end at all: the process was stopped at the elapsed ceiling, stopped by the hub
            // shutting down, or crashed. All three mean no commit (FR-017) — and each gets its own
            // reason, because each has a different fix (FR-018).
            string reason;
            if (exit.StoppedAtElapsedCeiling)
            {
                reason = settings.Limit.ElapsedReason();
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                reason = GracefulShutdown.InterruptedReason;
            }
            else
            {
                reason = exit.ExitCode is 0
                    ? "The run ended without reporting an outcome."
                    : $"The runner process exited with code {exit.ExitCode} before reporting an outcome. "
                      + "Nothing was committed. Its diagnostics are in the hub's log.";
            }

            return new RunOutcome.Failed(reason, toolCallCount);
        }

        if (exit.RunEnd.ToOutcome() is RunOutcomeKind.Failed)
        {
            return new RunOutcome.Failed(
                exit.RunEnd.FailureReason ?? "The run reported a failure with no reason.",
                toolCallCount);
        }

        // The runner reported success and then the process itself ended abnormally — a crash on
        // the way out, after `run_end` was already written. Trusting the reported outcome here
        // would commit a working tree the process never actually finished with (FR-017).
        if (exit.ExitCode is not 0)
        {
            return new RunOutcome.Failed(
                $"The runner reported a completed outcome but the process exited with code "
                + $"{exit.ExitCode}.",
                toolCallCount);
        }

        return treeChanged
            ? new RunOutcome.Changed(exit.RunEnd.CommitMessage, toolCallCount)
            : new RunOutcome.ChangedNothing(toolCallCount);
    }

    private async Task Settle(
        string taskId,
        RunOutcome outcome,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var settlement = await outcomes.Settle(taskId, outcome, cancellationToken);
        var endedAt = DateTimeOffset.UtcNow;
        var durationMs = (int)(endedAt - startedAt).TotalMilliseconds;

        // A commit, once made, is a fact of wiki history that nothing here can safely undo: unlike
        // every other settlement step, this persistence cannot be retried by falling through to
        // the outer catch's Failed+Reset path — the working tree already matches the new commit,
        // so a reset would no-op while the task got recorded as failed with no commit, leaving a
        // real wiki mutation that no task artifact accounts for. A short retry directly here is
        // the containment for that gap: most persistence failures this close to a successful
        // commit are transient (a locked database file, a momentary I/O error), and retrying the
        // exact same write is safe because EndRun is not otherwise called twice for one run.
        PersistEndRun(taskId, settlement, durationMs, endedAt);

        if (settlement.Commit is { } commit)
        {
            logger.LogInformation(
                "grimoire.wiki.committed {TaskId} {CommitSha} {FilesChanged}",
                taskId, commit.Sha, wiki.DiffOf(commit.Sha).Count);
        }

        logger.LogInformation(
            "grimoire.run.ended {TaskId} {Outcome} {FailureReason} {ToolCallCount} {DurationMs}",
            taskId,
            settlement.Succeeded ? "completed" : "failed",
            settlement.FailureReason,
            settlement.ToolCallCount,
            durationMs);
        logger.LogInformation(
            "grimoire.task.state_changed {TaskId} {State}",
            taskId, settlement.Succeeded ? "completed" : "failed");
    }

    private void PersistEndRun(string taskId, RunSettlement settlement, int durationMs, DateTimeOffset endedAt)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                var recorded = store.EndRun(
                    taskId,
                    settlement.Succeeded ? RunOutcomeKind.Completed : RunOutcomeKind.Failed,
                    settlement.FailureReason,
                    settlement.Commit,
                    durationMs,
                    endedAt);

                // Nothing to end: either no run was ever started — the gate refused before
                // `proceed` — or the run was already settled elsewhere, by the shutdown path. The
                // first still has to leave `queued`; the second must not be rewritten (FR-023).
                if (!recorded
                    && !(settlement.FailureReason is { } reason && store.FailTask(taskId, reason, endedAt))
                    && settlement.Commit is { } orphan)
                {
                    // Nothing a restart could do differently: the task is already settled, so the
                    // pending commit is not left for startup to retry.
                    store.DiscardPendingSettlement(taskId);
                    logger.LogCritical(
                        "The commit {CommitSha} for task {TaskId} exists in the wiki, but the task had "
                        + "already been settled without it. An operator needs to reconcile the task "
                        + "artifact with wiki history by hand.",
                        orphan.Sha, taskId);
                }

                return;
            }
            catch (Exception exception) when (settlement.Commit is not null && attempt < 3)
            {
                logger.LogWarning(exception,
                    "Retrying: the commit {CommitSha} for task {TaskId} exists in the wiki but recording "
                    + "it against the task failed on attempt {Attempt}.",
                    settlement.Commit.Sha, taskId, attempt);
                Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (Exception exception) when (settlement.Commit is not null)
            {
                // Out of retries. The commit stands regardless — this loudly says so rather than
                // letting the generic failure path silently no-op a reset over it.
                logger.LogCritical(exception,
                    "The commit {CommitSha} for task {TaskId} exists in the wiki, but the task could "
                    + "not be updated to reflect it after {Attempts} attempts. It is on record as "
                    + "pending, and the hub records it on the task when it next starts.",
                    settlement.Commit.Sha, taskId, attempt);
                throw;
            }
        }
    }

    private static string? GrantMismatch(ToolGrantEvent reported)
    {
        var actual = reported.Tools.Order(StringComparer.Ordinal).ToList();
        var expected = ConfiguredGrant.Order(StringComparer.Ordinal).ToList();

        return actual.SequenceEqual(expected, StringComparer.Ordinal)
            ? null
            : "The runner reported a tool grant the hub did not configure: "
              + $"[{string.Join(", ", actual)}] rather than [{string.Join(", ", expected)}].";
    }
}

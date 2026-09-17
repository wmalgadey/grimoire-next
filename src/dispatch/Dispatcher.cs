using Grimoire.Dispatch.Adapters;
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
        var sourceText = task.Source.TextForRun;
        if (sourceText is null)
        {
            // Nothing to hand the agent. The task already failed at retrieval (FR-003).
            return;
        }

        // "Is the run failing on its own account, or because the egress path is broken?" is a
        // question an operator has to be able to answer, and they cannot answer it from a run that
        // sat there until its elapsed ceiling. The SDK retries a connection failure with backoff,
        // so without this probe an unreachable endpoint is a hang rather than a reason (plan IV,
        // grimoire.run.model_endpoint_unreachable; TS-18).
        if (Unreachable(settings.ModelBaseUrl) is { } unreachable)
        {
            logger.LogWarning(
                "grimoire.run.model_endpoint_unreachable {TaskId} {Endpoint} {Status}",
                task.Id, settings.ModelBaseUrl, "unreachable");
            store.FailTask(task.Id, unreachable, DateTimeOffset.UtcNow);
            logger.LogInformation("grimoire.task.state_changed {TaskId} {State}", task.Id, "failed");
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var home = Path.Combine(Path.GetTempPath(), $"grimoire-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);

        var runner = new RunnerProcess(
            settings.WikiRepositoryPath,
            RunnerEnvironment.ForRun(
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

            var outcome = Interpret(exit, grantMismatch, toolCallCount);
            await Settle(task.Id, outcome, startedAt, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "grimoire.run.ended {TaskId} {Outcome}", task.Id, "failed");
            await Settle(
                task.Id,
                new RunOutcome.Failed($"The run could not be completed: {exception.Message}", toolCallCount),
                startedAt,
                cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(home, recursive: true);
            }
            catch (IOException)
            {
                // A leftover per-run directory is discarded with the container.
            }
        }
    }

    /// <summary>What a runner exit means for the artifact.</summary>
    private RunOutcome Interpret(RunnerExit exit, string? grantMismatch, int toolCallCount)
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
            // No run_end at all: the process crashed, was killed at the elapsed ceiling, or was
            // aborted. All three are the same thing here — no commit (FR-017).
            return new RunOutcome.Failed(
                exit.ExitCode is 0
                    ? "The run ended without reporting an outcome."
                    : settings.Limit.ElapsedReason(),
                toolCallCount);
        }

        if (exit.RunEnd.ToOutcome() is RunOutcomeKind.Failed)
        {
            return new RunOutcome.Failed(
                exit.RunEnd.FailureReason ?? "The run reported a failure with no reason.",
                toolCallCount);
        }

        return new RunOutcome.Changed(exit.RunEnd.CommitMessage, toolCallCount);
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

        store.EndRun(
            taskId,
            settlement.Succeeded ? RunOutcomeKind.Completed : RunOutcomeKind.Failed,
            settlement.FailureReason,
            settlement.Commit,
            durationMs,
            endedAt);

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

    /// <summary>
    /// Why the model endpoint cannot be reached, or <c>null</c> when it can. A TCP connect, not a
    /// model call: this asks whether the one permitted destination is there, which is the same
    /// question <c>/readyz</c> asks and a different one from whether the agent did its job.
    /// </summary>
    private static string? Unreachable(string modelBaseUrl)
    {
        if (!Uri.TryCreate(modelBaseUrl, UriKind.Absolute, out var uri))
        {
            return $"The model endpoint '{modelBaseUrl}' is not an absolute URL. "
                + "Check GRIMOIRE_MODEL_BASE_URL.";
        }

        try
        {
            using var socket = new System.Net.Sockets.TcpClient();
            if (!socket.ConnectAsync(uri.Host, uri.Port).Wait(TimeSpan.FromSeconds(5)))
            {
                return $"The model endpoint {uri.Host}:{uri.Port} did not answer within five seconds, "
                    + "so no run was dispatched. This is the egress path, not the agent.";
            }

            return null;
        }
        catch (Exception exception)
            when (exception is System.Net.Sockets.SocketException or AggregateException or IOException)
        {
            return $"The model endpoint {uri.Host}:{uri.Port} is unreachable, so no run was dispatched. "
                + "This is the egress path, not the agent.";
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

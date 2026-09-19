using System.Diagnostics;
using System.Text;

namespace Grimoire.Dispatch.Adapters;

/// <summary>How a runner process ended, from the hub's side.</summary>
/// <param name="RunEnd">
/// The run's own account of itself. <c>null</c> when the process crashed or was killed: a crash
/// produces no <c>run_end</c> at all, which the hub treats identically to an abort — no commit
/// (FR-017).
/// </param>
/// <param name="ExitCode">Non-zero means the process did not end normally.</param>
/// <param name="Diagnostics">The runner's stderr. Diagnostics only, never a source of task state.</param>
/// <param name="StoppedAtElapsedCeiling">
/// Whether the hub killed the process because the run reached its elapsed ceiling (FR-009) — as
/// opposed to the process crashing on its own, or the hub shutting down.
/// </param>
public sealed record RunnerExit(RunEndEvent? RunEnd, int ExitCode, string Diagnostics, bool StoppedAtElapsedCeiling = false);

/// <summary>
/// Spawns the agent runner, one process per run and never reused
/// (contracts/runner-protocol.md "Invocation").
/// </summary>
/// <remarks>
/// With <c>src/wiki/adapters/GitCli.cs</c>, one of only two places in the repository that use
/// <see cref="Process"/> — adapter confinement asserted by
/// <c>tests/architecture/AdapterConfinementTests.cs</c> (constitution V.3).
///
/// Two properties of the spawn carry requirements rather than convenience:
/// <list type="bullet">
/// <item><c>cwd</c> is the wiki working tree — the agent's entire filesystem world.</item>
/// <item><c>env</c> is <b>replaced</b>, not merged, so credential and host-environment scrubbing
/// is a property of the spawn rather than a habit of the caller (ADR-0007, ADR-0010).</item>
/// </list>
/// </remarks>
/// <param name="repositoryRoot">The wiki working tree, pinned as the child's <c>cwd</c>.</param>
/// <param name="environmentFor">
/// The run's environment, given the per-run home directory this adapter creates for it and
/// discards with it.
/// </param>
public sealed class RunnerProcess(string repositoryRoot, Func<string, RunnerEnvironment> environmentFor)
{
    /// <summary>The compiled runner the hub spawns.</summary>
    public const string EntryPoint = "src/agentrun/dist/main.js";

    /// <summary>
    /// Runs one agent run to its end.
    /// </summary>
    /// <param name="dispatch">
    /// The run's input. Sent after <c>instruction_loaded</c> and <c>tool_grant</c> have arrived
    /// and been persisted, immediately before <c>proceed</c>.
    /// </param>
    /// <param name="limit">The elapsed ceiling, enforced here by killing the child (FR-009).</param>
    /// <param name="onEvent">
    /// Called for each runner event as it arrives, so the tool-call record grows during the run
    /// rather than at its end (FR-021).
    /// </param>
    /// <param name="gate">
    /// Awaited after <c>tool_grant</c> arrives and before <c>proceed</c> is sent. This is the
    /// handshake that makes "recorded before the first model call" a property (FR-013, FR-014).
    /// </param>
    public async Task<RunnerExit> Run(
        DispatchMessage dispatch,
        RunLimit limit,
        Func<RunnerEvent, Task> onEvent,
        Func<Task> gate,
        CancellationToken cancellationToken)
    {
        // A home directory of the run's own, so nothing the SDK keeps there outlives the run or
        // reaches the next one. Named for its task, so whose it is can be read off the disk.
        var home = Path.Combine(Path.GetTempPath(), $"grimoire-run-{dispatch.TaskId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);

        try
        {
            return await Run(environmentFor(home), dispatch, limit, onEvent, gate, cancellationToken);
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

    private async Task<RunnerExit> Run(
        RunnerEnvironment environment,
        DispatchMessage dispatch,
        RunLimit limit,
        Func<RunnerEvent, Task> onEvent,
        Func<Task> gate,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo("node")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(environment.EntryPointPath);

        // Replaced, not merged: nothing of the hub's environment reaches the agent.
        info.Environment.Clear();
        foreach (var (name, value) in environment.Variables)
        {
            info.Environment[name] = value;
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("The agent runner could not be started.");

        var diagnostics = new StringBuilder();
        RunEndEvent? runEnd = null;

        var stderrPump = Task.Run(
            async () => diagnostics.Append(await process.StandardError.ReadToEndAsync(cancellationToken)),
            cancellationToken);

        using var elapsed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        elapsed.CancelAfter(limit.Elapsed);

        var gateOpened = false;
        try
        {
            await process.StandardInput.WriteLineAsync(RunnerProtocol.Serialise(dispatch));
            await process.StandardInput.FlushAsync(cancellationToken);

            while (await process.StandardOutput.ReadLineAsync(elapsed.Token) is { } line)
            {
                if (line.Trim().Length is 0)
                {
                    continue;
                }

                var runnerEvent = RunnerProtocol.ParseEvent(line);
                await onEvent(runnerEvent);

                if (runnerEvent is ToolGrantEvent && !gateOpened)
                {
                    gateOpened = true;

                    // A refusal here (e.g. a grant mismatch) leaves the child blocked on stdin
                    // waiting for `proceed`, which is then never sent: the catch below kills it
                    // before anything else happens, which is what makes "the model was never
                    // invoked" true rather than a race.
                    await gate();

                    await process.StandardInput.WriteLineAsync(RunnerProtocol.Serialise(new ProceedMessage()));
                    await process.StandardInput.FlushAsync(cancellationToken);
                }

                if (runnerEvent is RunEndEvent end)
                {
                    runEnd = end;
                }
            }

            await process.WaitForExitAsync(elapsed.Token);
        }
        catch (OperationCanceledException)
        {
            // The elapsed ceiling, or the hub shutting down. Either way the run is over and
            // nothing it wrote will be committed (FR-009, FR-017).
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new RunnerExit(
                null,
                ExitCodeOf(process),
                diagnostics.ToString(),
                StoppedAtElapsedCeiling: !cancellationToken.IsCancellationRequested);
        }
        catch
        {
            // Every other way out — a line that is not a protocol event, the gate refusing, the
            // store failing while an event is recorded. The hub has stopped listening to this run,
            // so the child must stop too: alive, it would keep writing into a working tree that is
            // about to be reset and that the next run starts from (FR-017, SC-003).
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await Task.WhenAny(stderrPump, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
        }

        return new RunnerExit(runEnd, ExitCodeOf(process), diagnostics.ToString());
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static int ExitCodeOf(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}

/// <summary>
/// The runner's environment, exactly as contracts/deployment.md fixes it. Nothing else: no
/// inherited process environment, no Anthropic credential, no git configuration, and no hub path
/// but the instruction file the runner loads.
/// </summary>
/// <param name="EntryPointPath">Absolute path to <c>src/agentrun/dist/main.js</c>.</param>
/// <param name="Variables">The complete environment the child gets.</param>
public sealed record RunnerEnvironment(string EntryPointPath, IReadOnlyDictionary<string, string> Variables)
{
    /// <summary>
    /// Builds the environment for one run.
    /// </summary>
    /// <param name="runId">Joins the proxy's access log to a task through <c>X-Grimoire-Run</c>.</param>
    /// <param name="modelBaseUrl">The proxy's model route.</param>
    /// <param name="modelToken">
    /// An <b>opaque internal token</b> for the proxy, never an Anthropic credential: no process in
    /// this deployment holds one (ADR-0010).
    /// </param>
    /// <param name="instructionPath">The instruction file the runner loads.</param>
    /// <param name="homeDirectory">A per-run directory, discarded with the run.</param>
    /// <param name="repositoryRoot">Used only to resolve the entry point, never handed to the child.</param>
    public static RunnerEnvironment ForRun(
        string runId,
        string modelBaseUrl,
        string modelToken,
        string instructionPath,
        string homeDirectory,
        string repositoryRoot)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = MinimalPath(Environment.GetEnvironmentVariable("PATH")),
            ["HOME"] = homeDirectory,
            ["ANTHROPIC_BASE_URL"] = modelBaseUrl,
            ["ANTHROPIC_AUTH_TOKEN"] = modelToken,
            ["ANTHROPIC_CUSTOM_HEADERS"] = $"X-Grimoire-Run: {runId}",
            // Required, not advisory: without it the SDK raises version checks, telemetry and
            // third-party requests outside the gateway path, which under a deny-all egress policy
            // become failed connections and blocked-connection noise (contracts/deployment.md).
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
            // The child's cwd is the wiki working tree, not the application root (`repositoryRoot`
            // below), so a relative instruction path has to be resolved here — otherwise the
            // default `src/instructions/ingest.md` is looked up inside the wiki and every run
            // fails before the model is ever called.
            ["GRIMOIRE_INSTRUCTION"] = Path.IsPathRooted(instructionPath)
                ? instructionPath
                : Path.Combine(repositoryRoot, instructionPath),
        };

        return new RunnerEnvironment(Path.Combine(repositoryRoot, RunnerProcess.EntryPoint), variables);
    }

    /// <summary>
    /// Enough to find <c>node</c> and the base system directories, and nothing of the hub's own
    /// <c>PATH</c> beyond that (contracts/deployment.md "Runner").
    /// </summary>
    public static string MinimalPath(string? hubPath)
    {
        var nodeDirectory = (hubPath ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(directory => File.Exists(Path.Combine(directory, "node")));

        return string.Join(
            Path.PathSeparator,
            new[] { nodeDirectory, "/usr/bin", "/bin" }.OfType<string>().Distinct(StringComparer.Ordinal));
    }
}

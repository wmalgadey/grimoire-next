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
public sealed record RunnerExit(RunEndEvent? RunEnd, int ExitCode, string Diagnostics);

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
public sealed class RunnerProcess(string repositoryRoot, RunnerEnvironment environment)
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
            return new RunnerExit(null, ExitCodeOf(process), diagnostics.ToString());
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
/// inherited process environment, no Anthropic credential, no git configuration, no hub paths.
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
            ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin",
            ["HOME"] = homeDirectory,
            ["ANTHROPIC_BASE_URL"] = modelBaseUrl,
            ["ANTHROPIC_AUTH_TOKEN"] = modelToken,
            ["ANTHROPIC_CUSTOM_HEADERS"] = $"X-Grimoire-Run: {runId}",
            // Required, not advisory: without it the SDK raises version checks, telemetry and
            // third-party requests outside the gateway path, which under a deny-all egress policy
            // become failed connections and blocked-connection noise (contracts/deployment.md).
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
            ["GRIMOIRE_INSTRUCTION"] = instructionPath,
        };

        return new RunnerEnvironment(Path.Combine(repositoryRoot, RunnerProcess.EntryPoint), variables);
    }
}

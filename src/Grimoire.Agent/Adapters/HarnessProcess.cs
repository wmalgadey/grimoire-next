using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Grimoire.Agent.Adapters;

/// <summary>Where the `claude` CLI is, and what it is told about this hub.</summary>
/// <param name="Executable">The CLI on the path, or an absolute path to it.</param>
/// <param name="WorkingDirectory">
/// A directory Grimoire owns — <b>not</b> the wiki. The agent reaches the wiki only through the
/// granted tools, and a working directory inside it would be a second door
/// (Constitution V.1, research.md R-11).
/// </param>
/// <param name="McpBaseAddress">Where the hub serves each run's tools, e.g. <c>http://127.0.0.1:5199</c>.</param>
public sealed record HarnessSettings(string Executable, string WorkingDirectory, Uri McpBaseAddress)
{
    public static HarnessSettings Default(Uri mcpBaseAddress) => new(
        "claude",
        Path.Combine(Path.GetTempPath(), "grimoire-runs"),
        mcpBaseAddress);
}

/// <summary>
/// The <c>claude</c> process: started, written to, interrupted, killed
/// (Constitution V.2, <c>contracts/agent-cli-protocol.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// This adapter decides nothing about a run. It reports what the CLI says and does what the hub
/// tells it: the ceilings, the run's state and the single nudge are all the hub's.
/// </para>
/// <para>
/// What the CLI's lines <em>mean</em> is <see cref="AgentTranscript"/>'s, beside this file and inside
/// the same adapter. Nothing here reads the protocol; this class owns the process and the two
/// things written to its stdin, and turns the events <see cref="AgentTranscript"/> produces into calls
/// on the hub's <see cref="RunReport"/>.
/// </para>
/// </remarks>
public sealed class HarnessProcess(HarnessSettings settings) : IAgentHarness
{
    /// <summary>
    /// How long the interrupted process is given to finish reporting the turn before it is killed.
    /// A constant rather than a setting: docs/product.md §4 rules out per-run tuning, and this is
    /// the backstop's trigger, not a budget.
    /// </summary>
    private static readonly TimeSpan KillAfter = TimeSpan.FromSeconds(10);

    private readonly Dictionary<Guid, Process> running = [];

    /// <summary>
    /// The runs a stop is already under way for. The hub raises the cost ceiling on every streamed
    /// usage past it, so a second and a third <see cref="StopAsync"/> for one run are the normal
    /// case; only the first sends the interrupt and waits for the kill backstop.
    /// </summary>
    private readonly HashSet<Guid> stopping = [];
    private readonly Lock gate = new();

    /// <summary>The argv of <c>contracts/agent-cli-protocol.md</c>, exactly.</summary>
    public static IReadOnlyList<string> ArgumentsFor(AgentDispatch dispatch, Uri mcpBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(mcpBaseAddress);

        var mcpConfig = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [AgentTranscript.ServerName] = new JsonObject
                {
                    ["type"] = "http",
                    ["url"] = new Uri(mcpBaseAddress, $"/mcp/runs/{dispatch.RunId}").ToString(),
                },
            },
        };

        return
        [
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--model", dispatch.Model,

            // The grant is the whole tool surface: no built-in tool exists in the run, and the
            // only tools that do are the ones the hub serves for it (GUARD-001, research.md R-03).
            "--tools", string.Empty,
            "--mcp-config", mcpConfig.ToJsonString(),
            "--strict-mcp-config",
            "--allowed-tools", $"{AgentTranscript.McpPrefix}*",
            "--permission-mode", "dontAsk",

            // No settings, hooks or CLAUDE.md from the machine reach the prompt (V.1).
            "--setting-sources", string.Empty,
            "--no-session-persistence",
        ];
    }

    public Task DispatchAsync(AgentDispatch dispatch, RunReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(report);

        Directory.CreateDirectory(settings.WorkingDirectory);

        var start = new ProcessStartInfo(settings.Executable)
        {
            WorkingDirectory = settings.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in ArgumentsFor(dispatch, settings.McpBaseAddress))
        {
            start.ArgumentList.Add(argument);
        }

        // An API key would bill per token through the API instead of the owner's subscription,
        // which DEC-001 rules out. Unset, the run is on the subscription or it does not start.
        start.Environment.Remove("ANTHROPIC_API_KEY");

        var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{settings.Executable} did not start");

        lock (gate)
        {
            running[dispatch.RunId] = process;
        }

        // Reported before a single byte is written to the child, because a kill an instant later
        // is exactly the case this identity is kept for: an agent nobody wrote down cannot be
        // found again at the next start-up (RUNS-006, research.md R-11).
        report.AgentProcessIs(dispatch.SubmissionId, IdentityOf(process));

        // stderr is redirected, so somebody has to read it: a pipe nobody drains fills at about
        // 64 KiB, and the CLI then blocks on its own diagnostics — stdout stops, the run stalls,
        // and no ceiling can tell that from a slow model. Nothing is done with the lines; what the
        // hub acts on arrives on stdout (contracts/agent-cli-protocol.md).
        _ = Task.Run(() => DrainAsync(process.StandardError), CancellationToken.None);

        return DispatchedAsync(process, dispatch, report, cancellationToken);
    }

    /// <summary>
    /// The prompt first and the reader after it. Both write to the same <c>StandardInput</c> — the
    /// reader sends the interrupt and closes stdin when <c>system/init</c> is refused — and a
    /// <c>StreamWriter</c> is not thread-safe, so the two must not overlap. Started the other way
    /// round, a refused init could close stdin underneath the prompt still being written to it.
    /// </summary>
    private async Task DispatchedAsync(
        Process process, AgentDispatch dispatch, RunReport report, CancellationToken cancellationToken)
    {
        await WriteUserMessageAsync(process, dispatch.Prompt, cancellationToken).ConfigureAwait(false);

        // The stream is read on its own; the run is under way and this call returns (INGEST-001).
        _ = Task.Run(() => ReadAsync(process, dispatch, report), CancellationToken.None);
    }

    private static async Task DrainAsync(StreamReader stderr)
    {
        try
        {
            while (await stderr.ReadLineAsync().ConfigureAwait(false) is not null)
            {
                // Read and let go. The point is that the pipe never fills.
            }
        }
        catch (Exception)
        {
            // Nothing awaits this, and a reader that has lost its process has nothing left to do.
        }
    }

    public Task NudgeAsync(Guid runId, CancellationToken cancellationToken)
    {
        // A further user message on stdin: the agent continues in the same session and inside the
        // same ceilings (RUNS-005, research.md R-11).
        var process = Find(runId);

        return process is null
            ? Task.CompletedTask
            : WriteUserMessageAsync(process, "No log entry for this run was found in log.md.", cancellationToken);
    }

    public Task NothingFurtherAsync(Guid runId, CancellationToken cancellationToken)
    {
        var process = Find(runId);
        if (process is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            // The CLI reads stdin for as long as it is open. Closing it is the whole of this: no
            // interrupt, because there is no call in flight to end — the agent has said its piece.
            process.StandardInput.Close();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Already closed, or the process is already gone.
        }

        // The backstop runs on its own and this call does not wait for it. It is made from inside
        // the reader, and waiting here for an exit would be waiting for a stream that the reader
        // is not reading while it waits.
        _ = Task.Run(() => KillIfItWillNotEndAsync(process), CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopAsync(Guid runId, CancellationToken cancellationToken)
    {
        var process = Find(runId);
        if (process is null)
        {
            return;
        }

        lock (gate)
        {
            // A stop already under way is not repeated. Without this the second call writes to the
            // stdin the first one closed, and waits a second time on the kill backstop.
            if (!stopping.Add(runId))
            {
                return;
            }
        }

        // The interrupt ends a call in flight; killing the process is the backstop, not the
        // mechanism, because a signal leaves the turn unfinished (GUARD-004, research.md R-11).
        var interrupt = new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = $"stop-{runId}",
            ["request"] = new JsonObject { ["subtype"] = "interrupt" },
        };

        try
        {
            await process.StandardInput.WriteLineAsync(interrupt.ToJsonString().AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Nothing further is coming, so the process may end once the interrupted turn is
            // finished reporting itself. Closing stdin is what tells it that.
            process.StandardInput.Close();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The process is already gone, or its stdin is already closed — Process.StandardInput
            // hands back the same writer every time, so a closed one throws ObjectDisposedException
            // rather than IOException. Either way the backstop below is all that is left.
        }

        // The interrupt is the mechanism and the kill is the backstop, in that order: a signal
        // leaves the turn unfinished, whereas the interrupt ends it and lets the run say how it
        // ended (contracts/agent-cli-protocol.md, research.md R-11). The process is given that
        // moment before it is killed.
        await KillIfItWillNotEndAsync(process).ConfigureAwait(false);
    }

    /// <summary>
    /// The backstop of <c>contracts/agent-cli-protocol.md</c>: a process that has not ended a
    /// while after its stdin was closed is killed. Never the mechanism — a signal leaves the turn
    /// unfinished, and by the time this fires the run has nothing left to say.
    /// </summary>
    private static async Task KillIfItWillNotEndAsync(Process process)
    {
        try
        {
            using var backstop = new CancellationTokenSource(KillAfter);
            await process.WaitForExitAsync(backstop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // It did not end on its own.
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            // It ended between the two calls, or it was never ours to kill.
        }
    }

    /// <summary>
    /// The identifier and the moment the process started — the pair, because the identifier alone
    /// is not an identity (research.md R-11). <c>StartTime</c> is local, and everything Grimoire
    /// compares is UTC.
    /// </summary>
    private static AgentProcessIdentity IdentityOf(Process process) =>
        new(process.Id, new DateTimeOffset(process.StartTime).ToUniversalTime());

    /// <summary>
    /// End an agent that outlived a stop Grimoire could not act on — and only that agent
    /// (RUNS-006).
    /// </summary>
    /// <remarks>
    /// The whole pair has to match. A process that is gone, or one that carries the number but
    /// started at another moment, is left alone: those numbers are reused, and after a reboot one
    /// almost certainly belongs to something else on the owner's machine (research.md R-11). The
    /// kill is the tree kill this adapter already performs at a ceiling, so the act is not new;
    /// no interrupt precedes it, because there is nothing left to interrupt — the Grimoire that
    /// could have read the answer is gone.
    /// <para>
    /// <b>A kill that fails is not swallowed.</b> Every other failure of <c>Kill</c> — a
    /// <c>Win32Exception</c> the operating system raises because the signal did not land, an
    /// <c>AggregateException</c> from a child of the tree — leaves the identity confirmed and the
    /// process alive, which is precisely the state RUNS-006 forbids to proceed from. It travels out
    /// of here, out of <c>HubApplication.RestoreAfterAStop</c>, and the hub does not start: better
    /// a Grimoire that refuses to come up than one that reads a run as failed and starts the next
    /// beside an agent it could not end. Failing to <em>read</em> the identity is the other case
    /// and is handled above, the other way round.
    /// </para>
    /// <para>
    /// <b>What this does not close</b>: the check and the kill are two operations, so a process
    /// that exits between them could in principle have its number taken by another before the
    /// signal lands. Binding the two together needs a per-operating-system primitive — Linux has
    /// <c>pidfd</c>, macOS has no equivalent — and research.md R-11 already turned such primitives
    /// down for that reason. The window is the microseconds between two calls and closing it needs
    /// the whole number space to wrap inside them; leaving the process alone instead, which is the
    /// only other portable answer, would leave an agent writing into the wiki with no ceiling on
    /// it and nothing left to end it. The narrower risk is the one taken.
    /// </para>
    /// </remarks>
    public void Terminate(AgentProcessIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Process process;

        try
        {
            process = Process.GetProcessById(identity.ProcessId);
        }
        catch (ArgumentException)
        {
            // Nothing is running under that number. The run reads failed as it would have anyway.
            return;
        }

        using (process)
        {
            try
            {
                if (process.HasExited || IdentityOf(process) != identity)
                {
                    // Alive, but not this run's agent. Left alone — this is the guard that keeps
                    // Grimoire from killing an unrelated program.
                    return;
                }
            }
            catch (Exception e) when (e is InvalidOperationException or SystemException)
            {
                // The identity cannot be read at all. Then it cannot be shown to be this run's
                // agent, and R-11 settles which way that falls: never end what is not provably
                // ours. An agent may outlive this, and that is the lesser harm against killing an
                // unrelated program on the owner's machine.
                return;
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended between the check and the kill. The window R-11 admits, and the one
                // outcome of it that is harmless: the agent is gone, which is what was wanted.
            }
        }
    }

    private Process? Find(Guid runId)
    {
        lock (gate)
        {
            return running.GetValueOrDefault(runId);
        }
    }

    private static Task WriteUserMessageAsync(Process process, string content, CancellationToken cancellationToken)
    {
        var message = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = content },
        };

        return WriteLineAsync(process, message.ToJsonString(), cancellationToken);
    }

    private static async Task WriteLineAsync(Process process, string line, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The CLI's stdout, one JSON object per line. Everything the hub learns about a run arrives
    /// here; what each line means is <see cref="AgentTranscript"/>'s, and what to do about it is the
    /// hub's.
    /// </summary>
    private async Task ReadAsync(Process process, AgentDispatch dispatch, RunReport report)
    {
        var stream = new AgentTranscript(dispatch.Grant);

        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var said = stream.Read(line);

                switch (said.Says)
                {
                    case TranscriptSays.InitIsNotAcceptable:
                        // Failed here, before the first model call (GUARD-001).
                        report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
                        await StopAsync(dispatch.RunId, CancellationToken.None).ConfigureAwait(false);
                        return;

                    case TranscriptSays.AgentReportedIn:
                        report.AgentReportedIn(dispatch.SubmissionId);
                        break;

                    case TranscriptSays.CostSoFar:
                        report.CostSoFar(dispatch.SubmissionId, said.TokensUsed);
                        break;

                    case TranscriptSays.AgentStopped:
                        report.CostSoFar(dispatch.SubmissionId, said.TokensUsed);

                        // The hub decides what a stop means — done, one nudge, or failed. A
                        // nudged run carries on, so more messages may follow this one.
                        await report.AgentStopped(dispatch.SubmissionId, said.EndedAbnormally).ConfigureAwait(false);
                        break;

                    case TranscriptSays.Nothing:
                    default:
                        break;
                }
            }

            await process.WaitForExitAsync().ConfigureAwait(false);

            // The stream is over and the process is gone, so its exit code can finally be read.
            // This is where a run ends: the hub puts the code together with what the result said
            // and what the log held, and all three have to agree for a run to be done. A run the
            // hub has already ended — one stopped at a ceiling — is unaffected; that ending stands.
            report.AgentExited(dispatch.SubmissionId, process.ExitCode);
        }
        catch (Exception)
        {
            // Nothing awaits this reader, so rethrowing would only surface later as an
            // unobserved task exception. The run has been ended, which is the part that matters
            // to the hub; the reader stops here.
            report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
        }
        finally
        {
            lock (gate)
            {
                running.Remove(dispatch.RunId);
                stopping.Remove(dispatch.RunId);
            }
        }
    }
}

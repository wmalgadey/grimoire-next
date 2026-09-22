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

        // The stream is read on its own; the run is under way and this call returns (INGEST-001).
        _ = Task.Run(() => ReadAsync(process, dispatch, report), CancellationToken.None);

        return WriteUserMessageAsync(process, dispatch.Prompt, cancellationToken);
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

    public async Task StopAsync(Guid runId, CancellationToken cancellationToken)
    {
        var process = Find(runId);
        if (process is null)
        {
            return;
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
        catch (IOException)
        {
            // The process is already gone; the backstop below is all that is left.
        }

        // The interrupt is the mechanism and the kill is the backstop, in that order: a signal
        // leaves the turn unfinished, whereas the interrupt ends it and lets the run say how it
        // ended (contracts/agent-cli-protocol.md, research.md R-11). The process is given that
        // moment before it is killed.
        try
        {
            using var backstop = new CancellationTokenSource(KillAfter);
            await process.WaitForExitAsync(backstop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // It did not end on its own.
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
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

            // The stream is over. Whatever the last message was, a run whose process has gone is
            // not going to report anything further: if the hub has already ended it this is a
            // no-op, and if it has not — a nudged run whose process died, say — it ends failed
            // here rather than reading running for ever.
            report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
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
            }
        }
    }
}

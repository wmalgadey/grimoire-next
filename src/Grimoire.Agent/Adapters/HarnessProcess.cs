using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
/// The only place the <c>claude</c> process and its newline-delimited JSON appear
/// (Constitution V.2, <c>contracts/agent-cli-protocol.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// This adapter decides nothing about a run. It reports what the CLI says and does what the hub
/// tells it: the ceilings, the run's state and the single nudge are all the hub's.
/// </para>
/// <para>
/// The one judgment made here is the one that cannot be made anywhere else: the CLI namespaces
/// every MCP tool as <c>mcp__wiki__&lt;name&gt;</c>, and this file owns that mapping. It maps
/// before it compares, and a reported surface that is not the grant ends the run failed before its
/// first model call (GUARD-001, data-model.md §ToolGrant).
/// </para>
/// </remarks>
public sealed class HarnessProcess(HarnessSettings settings) : IAgentHarness
{
    /// <summary>The prefix the CLI puts on every MCP tool. Known here and nowhere else.</summary>
    public const string McpPrefix = "mcp__wiki__";

    private const string ServerName = "wiki";
    private const string InterruptCapability = "interrupt_receipt_v1";

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
                [ServerName] = new JsonObject
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
            "--allowed-tools", $"{McpPrefix}*",
            "--permission-mode", "dontAsk",

            // No settings, hooks or CLAUDE.md from the machine reach the prompt (V.1).
            "--setting-sources", string.Empty,
            "--no-session-persistence",
        ];
    }

    /// <summary>
    /// Whether what <c>system/init</c> reported is this run's grant. The bare names the grant
    /// records are mapped to the prefixed form the CLI uses before they are compared.
    /// </summary>
    public static bool SurfaceIsTheGrant(ToolGrant grant, IEnumerable<string> reported)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(reported);

        return grant.IsTheSurface(
            reported.Select(t => t.StartsWith(McpPrefix, StringComparison.Ordinal) ? t[McpPrefix.Length..] : t));
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
    /// here.
    /// </summary>
    private async Task ReadAsync(Process process, AgentDispatch dispatch, RunReport report)
    {
        var reportedIn = false;
        var ended = false;
        long streamed = 0;

        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (Parse(line) is not { } message || message["type"]?.GetValue<string>() is not { } type)
                {
                    continue;
                }

                switch (type)
                {
                    case "system" when message["subtype"]?.GetValue<string>() == "init":
                        if (!InitIsAcceptable(message, dispatch.Grant))
                        {
                            // Failed here, before the first model call (GUARD-001).
                            ended = true;
                            report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
                            await StopAsync(dispatch.RunId, CancellationToken.None).ConfigureAwait(false);
                            return;
                        }

                        reportedIn = true;
                        report.AgentReportedIn(dispatch.SubmissionId);
                        break;

                    case "stream_event":
                        streamed = Math.Max(streamed, StreamedTotal(message));
                        report.CostSoFar(dispatch.SubmissionId, streamed);
                        break;

                    case "result":
                        // modelUsage is the authority; the streamed total was only a floor (R-04).
                        streamed = Math.Max(streamed, Ceilings.CostOf(ModelUsage(message)));
                        report.CostSoFar(dispatch.SubmissionId, streamed);

                        ended = true;
                        await report.AgentStopped(dispatch.SubmissionId, EndedAbnormally(message)).ConfigureAwait(false);
                        ended = false;
                        break;

                    default:
                        break;
                }
            }

            await process.WaitForExitAsync().ConfigureAwait(false);

            if (!ended && process.ExitCode != 0)
            {
                report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
            }
            else if (!ended && !reportedIn)
            {
                // The process said nothing at all.
                report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
            }
        }
        catch (Exception) when (!ended)
        {
            report.RunEnded(dispatch.SubmissionId, RunOutcome.Failed);
            throw;
        }
        finally
        {
            lock (gate)
            {
                running.Remove(dispatch.RunId);
            }
        }
    }

    private static JsonObject? Parse(string line)
    {
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            // A line that is not JSON is not a message; the CLI's own diagnostics go to stderr.
            return null;
        }
    }

    /// <summary>
    /// What <c>system/init</c> has to say before a run may proceed: a tool surface that is the
    /// grant, the wiki server connected, and an interrupt we can actually send.
    /// </summary>
    private static bool InitIsAcceptable(JsonObject init, ToolGrant grant) =>
        SurfaceIsTheGrant(grant, Strings(init["tools"]))
        && WikiServerIsConnected(init["mcp_servers"])
        && Strings(init["capabilities"]).Contains(InterruptCapability, StringComparer.Ordinal);

    private static bool WikiServerIsConnected(JsonNode? servers) =>
        servers is JsonArray listed && listed.Any(IsConnectedWikiServer);

    private static bool IsConnectedWikiServer(JsonNode? server) =>
        server is JsonObject described
        && described["name"]?.GetValue<string>() == ServerName
        && described["status"]?.GetValue<string>() == "connected";

    private static IReadOnlyList<string> Strings(JsonNode? array) =>
        array is JsonArray listed
            ? [.. listed.Select(n => n?.GetValue<string>()).OfType<string>()]
            : [];

    private static long StreamedTotal(JsonObject message)
    {
        if (message["event"]?["usage"] is not JsonObject usage)
        {
            return 0;
        }

        return Field(usage, "input_tokens")
            + Field(usage, "output_tokens")
            + Field(usage, "cache_read_input_tokens")
            + Field(usage, "cache_creation_input_tokens");
    }

    /// <summary>
    /// Every entry of the result's <c>modelUsage</c> — all models, the CLI's own background calls
    /// included, because a call the run never asked for is still the run's doing (R-04).
    /// </summary>
    private static IEnumerable<ModelTokens> ModelUsage(JsonObject result)
    {
        if (result["modelUsage"] is not JsonObject usage)
        {
            yield break;
        }

        foreach (var (_, value) in usage)
        {
            if (value is JsonObject model)
            {
                yield return new ModelTokens(
                    Field(model, "inputTokens"),
                    Field(model, "outputTokens"),
                    Field(model, "cacheReadInputTokens"),
                    Field(model, "cacheCreationInputTokens"));
            }
        }
    }

    /// <summary>An ending the agent did not choose: an aborted stream, or a subtype that is not success.</summary>
    private static bool EndedAbnormally(JsonObject result) =>
        result["terminal_reason"]?.GetValue<string>() is "aborted_streaming"
        || result["subtype"]?.GetValue<string>() is { } subtype && subtype != "success";

    private static long Field(JsonObject node, string name) =>
        node[name] is { } value && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
}

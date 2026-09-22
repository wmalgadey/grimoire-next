using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grimoire.Agent.Adapters;

/// <summary>What one line of the CLI's stream tells the hub.</summary>
public enum TranscriptSays
{
    /// <summary>Nothing this hub acts on — a line that is not JSON, or a message of another type.</summary>
    Nothing,

    /// <summary><c>system/init</c>, and what it reported is acceptable. The run may proceed.</summary>
    AgentReportedIn,

    /// <summary>
    /// <c>system/init</c>, and what it reported is not acceptable: a tool surface that is not the
    /// grant, a surface or a capability list that cannot be read as names at all, the wiki server
    /// not connected, or no interrupt to send (GUARD-001, GUARD-004). The run ends failed on any
    /// of them, and the name says the judgment rather than one of its reasons.
    /// </summary>
    InitIsNotAcceptable,

    /// <summary>What the run has caused so far, in tokens.</summary>
    CostSoFar,

    /// <summary>The agent has stopped. The hub reads the log and decides (RUNS-005).</summary>
    AgentStopped,
}

/// <summary>
/// One line of the CLI's stream, read as the port event it carries.
/// </summary>
/// <param name="Says">Which event it is.</param>
/// <param name="TokensUsed">
/// Every token the run has caused up to and including this line. Never goes backwards: the CLI's
/// streamed <c>usage</c> is cumulative within a response, so the running figure is the highest
/// seen and not the sum of the deltas (research.md R-04).
/// </param>
/// <param name="EndedAbnormally">
/// On <see cref="TranscriptSays.AgentStopped"/>: the agent did not stop of its own accord — an
/// interrupted stream, or a subtype that is not success.
/// </param>
public sealed record TranscriptEvent(TranscriptSays Says, long TokensUsed = 0, bool EndedAbnormally = false);

/// <summary>
/// The CLI's newline-delimited JSON, read as port events. A line goes in, an event comes out; no
/// process, no file, no clock (<c>contracts/agent-cli-protocol.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the only judgment <c>HarnessProcess</c> makes about a run, and it is made here so that
/// it can be read against recorded lines without a process: whether <c>system/init</c> is
/// acceptable, what a turn has cost, and whether the agent stopped of its own accord. What the hub
/// then <em>does</em> about each — the states, the ceilings, the single nudge — stays the hub's.
/// </para>
/// <para>
/// It keeps one thing between lines: the highest cost seen so far. That is what makes the streamed
/// <c>usage</c>, which is cumulative within a response, add up to a turn rather than to a multiple
/// of it.
/// </para>
/// <para>
/// The CLI namespaces every MCP tool as <c>mcp__wiki__&lt;name&gt;</c>, and this file owns that
/// mapping. It maps before it compares, and a reported surface that is not the grant ends the run
/// failed before its first model call (GUARD-001, data-model.md §ToolGrant).
/// </para>
/// </remarks>
public sealed class AgentTranscript(ToolGrant grant)
{
    /// <summary>The prefix the CLI puts on every MCP tool. Known here and nowhere else.</summary>
    public const string McpPrefix = "mcp__wiki__";

    /// <summary>The name the hub's tool server is given in the CLI's MCP configuration.</summary>
    public const string ServerName = "wiki";

    private const string InterruptCapability = "interrupt_receipt_v1";

    /// <summary>The highest cost this run has reported, across every line read so far.</summary>
    private long spent;

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

    /// <summary>What this line tells the hub.</summary>
    public TranscriptEvent Read(string line)
    {
        if (Parse(line) is not { } message || message["type"]?.GetValue<string>() is not { } type)
        {
            return new TranscriptEvent(TranscriptSays.Nothing);
        }

        switch (type)
        {
            case "system" when message["subtype"]?.GetValue<string>() == "init":
                return new TranscriptEvent(
                    InitIsAcceptable(message, grant) ? TranscriptSays.AgentReportedIn : TranscriptSays.InitIsNotAcceptable);

            case "stream_event":
                spent = Math.Max(spent, StreamedTotal(message));
                return new TranscriptEvent(TranscriptSays.CostSoFar, spent);

            case "result":
                // modelUsage is the authority; the streamed total was only a floor (R-04).
                spent = Math.Max(spent, Ceilings.CostOf(ModelUsage(message)));
                return new TranscriptEvent(TranscriptSays.AgentStopped, spent, EndedAbnormally(message));

            default:
                return new TranscriptEvent(TranscriptSays.Nothing);
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
    /// grant, the wiki server connected, and an interrupt we can actually send. A <c>tools</c> or
    /// <c>capabilities</c> that is not an array of names is not read past — see
    /// <see cref="Strings"/>.
    /// </summary>
    private static bool InitIsAcceptable(JsonObject init, ToolGrant grant) =>
        Strings(init["tools"]) is { } tools
        && SurfaceIsTheGrant(grant, tools)
        && WikiServerIsConnected(init["mcp_servers"])
        && Strings(init["capabilities"]) is { } capabilities
        && capabilities.Contains(InterruptCapability, StringComparer.Ordinal);

    private static bool WikiServerIsConnected(JsonNode? servers) =>
        servers is JsonArray listed && listed.Any(IsConnectedWikiServer);

    private static bool IsConnectedWikiServer(JsonNode? server) =>
        server is JsonObject described
        && described["name"]?.GetValue<string>() == ServerName
        && described["status"]?.GetValue<string>() == "connected";

    /// <summary>
    /// The names a JSON array holds, or <c>null</c> when it is not an array of strings. An element
    /// that is not a string is not a name that can be compared, and dropping it would let a
    /// <c>tools</c> of the granted names plus a <c>null</c> pass as the grant. What cannot be read
    /// is refused, not read around (GUARD-001).
    /// </summary>
    private static List<string>? Strings(JsonNode? array)
    {
        if (array is not JsonArray listed)
        {
            return null;
        }

        var names = new List<string>(listed.Count);

        foreach (var element in listed)
        {
            if (element is not JsonValue value || !value.TryGetValue<string>(out var name))
            {
                return null;
            }

            names.Add(name);
        }

        return names;
    }

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

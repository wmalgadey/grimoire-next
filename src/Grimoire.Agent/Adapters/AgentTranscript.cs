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

    /// <summary>
    /// A complete <c>assistant</c> or <c>user</c> message held things the run did: tool calls, what
    /// they returned, the agent's own text (RUNS-009).
    /// </summary>
    /// <remarks>
    /// One value for all of them rather than one per kind, because <c>message.content</c> is an array
    /// and one line can carry several blocks of different kinds — three values could not describe
    /// such a line, and splitting it into three reads would lose the order the blocks arrived in.
    /// </remarks>
    MomentsHappened,
}

/// <summary>
/// One line of the CLI's stream, read as the port event it carries.
/// </summary>
/// <param name="Says">Which event it is.</param>
/// <param name="TokensUsed">
/// Every token the run has caused up to and including this line. Never goes backwards. The CLI's
/// streamed <c>usage</c> is cumulative within one response and starts again at the next, and the
/// <c>result</c>'s <c>modelUsage</c> is cumulative across the whole session — so the running
/// figure is the last reconciled session total plus this response's own, and neither a sum of the
/// deltas nor a sum of the results (research.md R-04,
/// <c>contracts/agent-cli-protocol.md</c> §What the two usage figures count).
/// </param>
/// <param name="EndedAbnormally">
/// On <see cref="TranscriptSays.AgentStopped"/>: the agent did not stop of its own accord — an
/// interrupted stream, or a subtype that is not success.
/// </param>
public sealed record TranscriptEvent(TranscriptSays Says, long TokensUsed = 0, bool EndedAbnormally = false)
{
    /// <summary>
    /// What this line says the run did, in the order the blocks arrived. Empty for every line that is
    /// not a complete <c>assistant</c> or <c>user</c> message (RUNS-009).
    /// </summary>
    public IReadOnlyList<TranscriptMoment> Moments { get; init; } = [];

    /// <summary>
    /// What the run has spent per model, as a <c>result</c>'s <c>modelUsage</c> reports it. Empty for
    /// every other line, including a streamed usage, which carries a total and no breakdown
    /// (RUNS-008, DEC-015).
    /// </summary>
    public IReadOnlyDictionary<string, ModelTokens> TokensPerModel { get; init; } =
        new Dictionary<string, ModelTokens>(StringComparer.Ordinal);
}

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
    /// The session total the last <c>result</c> reconciled to. Every turn after it streams on top
    /// of this rather than starting the run's counter again.
    /// </summary>
    private long reconciled;

    /// <summary>
    /// The tool the last <c>tool_use</c> block named, so that the result following it can say which
    /// call returned. A run makes one call at a time in the order the stream reports it, which is what
    /// makes order enough and an identifier in the record unnecessary (data-model.md §RunMoment).
    /// </summary>
    private string? lastToolCalled;

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
        if (Parse(line) is not { } message || Text(message["type"]) is not { } type)
        {
            return new TranscriptEvent(TranscriptSays.Nothing);
        }

        switch (type)
        {
            case "system" when Text(message["subtype"]) == "init":
                return new TranscriptEvent(
                    InitIsAcceptable(message, grant) ? TranscriptSays.AgentReportedIn : TranscriptSays.InitIsNotAcceptable);

            case "system" when IsThereAndUnreadable(message["subtype"]):
                // It may be the init, and there is no way to tell. An init that is not recognised
                // is a tool surface that is never checked, so the run does not get to proceed on
                // the strength of a line nobody could read (GUARD-001).
                return new TranscriptEvent(TranscriptSays.InitIsNotAcceptable);

            case "stream_event":
                // The streamed usage is cumulative within one response and starts again at the
                // next, so a nudged run's second turn streams from nothing. Added to what the last
                // result reconciled to, rather than compared against it: measured, turn two's
                // 57 895 beside turn one's 51 094 is a run that has caused 108 989, and a bare
                // Math.Max would have read it as 57 895 until the next result corrected it.
                spent = Math.Max(spent, reconciled + StreamedTotal(message));
                return new TranscriptEvent(TranscriptSays.CostSoFar, spent);

            case "result":
                // modelUsage is the authority and it is cumulative across the session, so the
                // whole run's cost is the last one and not the sum of them (R-04, measured).
                var perModel = ModelUsage(message);
                reconciled = Math.Max(reconciled, Ceilings.CostOf(perModel.Values));
                spent = Math.Max(spent, reconciled);
                return new TranscriptEvent(TranscriptSays.AgentStopped, spent, EndedAbnormally(message))
                {
                    // The breakdown travels with the total it is the sum of, so the record's tail
                    // cannot name models that do not add up to the figure beside them (RUNS-008).
                    TokensPerModel = perModel,
                };

            // What the run did, from the CLI's complete messages: every tool_use and text block of an
            // `assistant` message, and every tool_result block of a `user` one. The complete message
            // arrives for every block, so nothing is assembled from the partial stream — which stays
            // the cost ceiling's alone, as it was added for (research.md R-03).
            //
            // A `user` line on stdout is a tool result and never something Grimoire said: measured,
            // the CLI does not echo what is written to its stdin, so no moment can appear twice.
            case "assistant" or "user" when Moments(message) is { Count: > 0 } moments:
                return new TranscriptEvent(TranscriptSays.MomentsHappened) { Moments = moments };

            default:
                return new TranscriptEvent(TranscriptSays.Nothing);
        }
    }

    /// <summary>
    /// The moments one complete message holds, in the order its blocks arrived (RUNS-009).
    /// </summary>
    /// <remarks>
    /// <c>thinking</c> blocks are not read: measured, the complete message carries an empty
    /// <c>thinking</c> and a signature blob, and there is nothing in it a person reads (research.md
    /// R-05). Any other block kind is passed over the same way — what the record holds is what the
    /// run did, and a block this hub cannot name is not something it did.
    /// </remarks>
    private List<TranscriptMoment> Moments(JsonObject message)
    {
        var moments = new List<TranscriptMoment>();

        if (message["message"]?["content"] is not JsonArray blocks)
        {
            return moments;
        }

        foreach (var block in blocks.OfType<JsonObject>())
        {
            switch (Text(block["type"]))
            {
                case "tool_use":
                    // The name is kept for the result that follows. The CLI's `tool_use_id` is read
                    // to nothing: a run makes one call at a time in the order the stream reports it,
                    // and an identifier in the record would be a field with no reader (II.1).
                    lastToolCalled = Text(block["name"]);
                    moments.Add(new TranscriptMoment(
                        RunMomentKind.ToolCalled, lastToolCalled, block["input"]?.ToJsonString()));
                    break;

                case "tool_result":
                    moments.Add(new TranscriptMoment(
                        RunMomentKind.ToolReturned, lastToolCalled, ResultContent(block["content"])));
                    break;

                case "text":
                    moments.Add(new TranscriptMoment(RunMomentKind.AgentSaid, Tool: null, Text(block["text"])));
                    break;

                default:
                    break;
            }
        }

        return moments;
    }

    /// <summary>
    /// What a <c>tool_result</c> returned: a string is itself, and an array is the text of its text
    /// blocks, joined — the protocol allows both.
    /// </summary>
    /// <remarks>
    /// Anything else comes back <c>null</c>, which the record writes as a result that could not be
    /// read. Refused rather than read around, the way a <c>tools</c> array that is not names already
    /// is (GUARD-001's precedent): a result nobody could read must not pass for a call that returned
    /// nothing.
    /// </remarks>
    private static string? ResultContent(JsonNode? content) => content switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonArray blocks => string.Join(
            '\n',
            blocks.OfType<JsonObject>()
                .Where(b => Text(b["type"]) == "text")
                .Select(b => Text(b["text"]))
                .OfType<string>()),
        _ => null,
    };

    /// <summary>
    /// A node read as a string, or null where it is anything else. <c>GetValue&lt;string&gt;</c>
    /// throws on a node of another type, and a line this cannot make sense of is a line that says
    /// nothing — not one that takes the reader down with it, orphaning the process it was reading.
    /// </summary>
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

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
        && Text(described["name"]) == ServerName
        && Text(described["status"]) == "connected";

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
    private static Dictionary<string, ModelTokens> ModelUsage(JsonObject result)
    {
        var usage = new Dictionary<string, ModelTokens>(StringComparer.Ordinal);

        if (result["modelUsage"] is not JsonObject reported)
        {
            return usage;
        }

        foreach (var (name, value) in reported)
        {
            if (value is JsonObject model)
            {
                usage[name] = new ModelTokens(
                    Field(model, "inputTokens"),
                    Field(model, "outputTokens"),
                    Field(model, "cacheReadInputTokens"),
                    Field(model, "cacheCreationInputTokens"));
            }
        }

        return usage;
    }

    /// <summary>An ending the agent did not choose: an aborted stream, or a subtype that is not success.</summary>
    /// <remarks>
    /// A field that is there and cannot be read as a name counts as an ending the agent did not
    /// choose. Absent and unreadable are not the same thing: absent is the CLI saying nothing
    /// about it, unreadable is a result whose ending nobody can establish — and a run whose
    /// ending cannot be established did not stop of its own accord (GUARD-004).
    /// </remarks>
    private static bool EndedAbnormally(JsonObject result) =>
        IsThereAndUnreadable(result["terminal_reason"])
        || IsThereAndUnreadable(result["subtype"])
        || Text(result["terminal_reason"]) is "aborted_streaming"
        || Text(result["subtype"]) is { } subtype && subtype != "success";

    /// <summary>A field that is present and is not a string — there, and not readable.</summary>
    private static bool IsThereAndUnreadable(JsonNode? node) => node is not null && Text(node) is null;

    private static long Field(JsonObject node, string name) =>
        node[name] is { } value && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
}

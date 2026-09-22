namespace Grimoire.Fast.Tests;

/// <summary>
/// Lines the R-11 spike recorded from <c>claude</c> 2.1.240, as text.
/// </summary>
/// <remarks>
/// <para>
/// These are the CLI's own words, not a shape invented here: they are copied from
/// <c>research.md</c> R-11 and R-04 and from <c>contracts/agent-cli-protocol.md</c>, which are
/// where the spike wrote down what it saw. Two edits were needed to make them a run of
/// <em>this</em> hub, and they are the only ones:
/// </para>
/// <list type="bullet">
/// <item>
/// the spike's stub MCP server offered a single tool, so its <c>init</c> reported
/// <c>["mcp__wiki__append_log"]</c>; <see cref="Init"/> carries this run's grant instead, in the
/// same prefixed spelling. The one-tool line is kept verbatim as <see cref="InitOfTheSpikesStub"/>.
/// </item>
/// <item>
/// the contract writes the main model's key as the placeholder <c>claude-&lt;model&gt;-&lt;yyyymmdd&gt;</c>;
/// <see cref="Result"/> carries the pinned id the Fast suite dispatches with
/// (<see cref="FastHub.Model"/>). Every number is the recorded one.
/// </item>
/// </list>
/// <para>
/// They are written out one line at a time and nothing is assembled from parts: a fixture that is
/// built is a fixture that can be built wrong, and the point of these is that they are what came
/// back.
/// </para>
/// </remarks>
internal static class RecordedTranscript
{
    /// <summary><c>system/init</c>: the grant reported, the wiki server connected, an interrupt to send.</summary>
    internal const string Init =
        """
        {"type":"system","subtype":"init","session_id":"8f3c1b70","tools":["mcp__wiki__list_pages","mcp__wiki__read_page","mcp__wiki__write_page","mcp__wiki__write_index","mcp__wiki__append_log"],"mcp_servers":[{"name":"wiki","status":"connected"}],"capabilities":["interrupt_receipt_v1"]}
        """;

    /// <summary>The spike's own <c>init</c>, whose stub server offered one tool of the five.</summary>
    internal const string InitOfTheSpikesStub =
        """
        {"type":"system","subtype":"init","session_id":"8f3c1b70","tools":["mcp__wiki__append_log"],"mcp_servers":[{"name":"wiki","status":"connected"}],"capabilities":["interrupt_receipt_v1"]}
        """;

    /// <summary><see cref="Init"/> with one tool the grant does not name.</summary>
    internal const string InitWithAToolOutsideTheGrant =
        """
        {"type":"system","subtype":"init","session_id":"8f3c1b70","tools":["mcp__wiki__list_pages","mcp__wiki__read_page","mcp__wiki__write_page","mcp__wiki__write_index","mcp__wiki__append_log","Bash"],"mcp_servers":[{"name":"wiki","status":"connected"}],"capabilities":["interrupt_receipt_v1"]}
        """;

    /// <summary><see cref="Init"/> with the wiki server reported, but not connected.</summary>
    internal const string InitWithTheWikiServerDown =
        """
        {"type":"system","subtype":"init","session_id":"8f3c1b70","tools":["mcp__wiki__list_pages","mcp__wiki__read_page","mcp__wiki__write_page","mcp__wiki__write_index","mcp__wiki__append_log"],"mcp_servers":[{"name":"wiki","status":"failed"}],"capabilities":["interrupt_receipt_v1"]}
        """;

    /// <summary>
    /// <see cref="Init"/> with the wiki connected and a second server that is not. The CLI reports
    /// every server it was configured with, and a hub may be configured with more than the wiki's.
    /// </summary>
    internal const string InitWithASecondServerDown =
        """
        {"type":"system","subtype":"init","session_id":"8f3c1b70","tools":["mcp__wiki__list_pages","mcp__wiki__read_page","mcp__wiki__write_page","mcp__wiki__write_index","mcp__wiki__append_log"],"mcp_servers":[{"name":"wiki","status":"connected"},{"name":"notes","status":"failed"}],"capabilities":["interrupt_receipt_v1"]}
        """;

    /// <summary><see cref="Init"/> from a CLI that does not advertise the interrupt.</summary>
    internal const string InitWithoutTheInterrupt =
        """
        {"type":"system","subtype":"init","session_id":"8f3c1b70","tools":["mcp__wiki__list_pages","mcp__wiki__read_page","mcp__wiki__write_page","mcp__wiki__write_index","mcp__wiki__append_log"],"mcp_servers":[{"name":"wiki","status":"connected"}],"capabilities":["hooks_v1"]}
        """;

    /// <summary>
    /// The three <c>message_delta</c> events of the spike's response. The middle one is the line
    /// <c>contracts/agent-cli-protocol.md</c> records; the other two are the same counter earlier
    /// and later in the same response, which is the growth the spike observed. Cumulative, not
    /// deltas: the third already contains the first two.
    /// </summary>
    internal static readonly string[] StreamedUsage =
    [
        """
        {"type":"stream_event","event":{"type":"message_delta","usage":{"input_tokens":10,"output_tokens":41,"cache_read_input_tokens":0}}}
        """,
        """
        {"type":"stream_event","event":{"type":"message_delta","usage":{"input_tokens":10,"output_tokens":126,"cache_read_input_tokens":0}}}
        """,
        """
        {"type":"stream_event","event":{"type":"message_delta","usage":{"input_tokens":10,"output_tokens":310,"cache_read_input_tokens":0}}}
        """,
    ];

    /// <summary>What each line of <see cref="StreamedUsage"/> puts the run's cost at.</summary>
    internal static readonly long[] StreamedUsageTotals = [10 + 41, 10 + 126, 10 + 310];

    /// <summary>
    /// The turn's <c>result</c>: <c>modelUsage</c> with the pinned model and one background call
    /// of the CLI's own, which the run never asked for (R-04).
    /// </summary>
    internal const string Result =
        """
        {"type":"result","subtype":"success","terminal_reason":"completed","usage":{"input_tokens":40112,"output_tokens":3190,"cache_read_input_tokens":22016,"cache_creation_input_tokens":7228},"modelUsage":{"claude-opus-4-5-20251101":{"inputTokens":40112,"outputTokens":3190,"cacheReadInputTokens":22016,"cacheCreationInputTokens":7228},"claude-haiku-4-5-20251001":{"inputTokens":897,"outputTokens":12,"cacheReadInputTokens":0,"cacheCreationInputTokens":0}},"permission_denials":[]}
        """;

    /// <summary>The pinned model's four fields in <see cref="Result"/>.</summary>
    internal const long PinnedModelTotal = 40112 + 3190 + 22016 + 7228;

    /// <summary>The background call's four fields in <see cref="Result"/>.</summary>
    internal const long BackgroundCallTotal = 897 + 12 + 0 + 0;

    /// <summary>
    /// The <c>modelUsage</c> of R-04's one-turn probe, verbatim. <c>thinkingTokens</c> is a
    /// breakdown of <c>outputTokens</c> and not an addition to it, which is why the recorded cost
    /// of that run is <see cref="OneTurnProbeTotal"/> and not 33 more.
    /// </summary>
    internal const string ResultOfTheOneTurnProbe =
        """
        {"type":"result","subtype":"success","terminal_reason":"completed","modelUsage":{"claude-haiku-4-5-20251001":{"inputTokens":10,"outputTokens":42,"thinkingTokens":33,"cacheReadInputTokens":0,"cacheCreationInputTokens":6612,"canonicalModel":"claude-haiku-4-5","provider":"firstParty"}}}
        """;

    /// <summary>The 6 664 tokens R-04 records for that probe.</summary>
    internal const long OneTurnProbeTotal = 10 + 42 + 0 + 6612;

    /// <summary>
    /// What the spike saw 0.9 s after sending the interrupt: the turn ended without the agent
    /// choosing to end it.
    /// </summary>
    internal const string ResultOfAnInterruptedTurn =
        """
        {"type":"result","subtype":"error_during_execution","terminal_reason":"aborted_streaming","output_tokens":0}
        """;

    /// <summary>A message the hub reads nothing from.</summary>
    internal const string Assistant =
        """
        {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Reading the wiki."}]}}
        """;

    /// <summary>The CLI's own diagnostics go to stderr; a line like this is not a message.</summary>
    internal const string NotJson = "Loaded 3 MCP servers in 412ms";
}

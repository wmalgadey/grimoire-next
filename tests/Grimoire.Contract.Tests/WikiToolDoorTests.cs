using Grimoire.Agent;
using ModelContextProtocol.Client;

namespace Grimoire.Contract.Tests;

/// <summary>
/// What each of the hub's two MCP doors <b>actually serves</b>, read over a real session the way the
/// agent's CLI reads it (GUARD-002, GUARD-005).
/// </summary>
/// <remarks>
/// <para>
/// Contract, and not Fast, for two reasons. The protocol handshake is the real thing rather than a
/// reading of our own objects — the transport is the boundary (Constitution III.4) — and it costs
/// seconds per session, which the Fast suite's 15 s in total does not have (III.7). No sign-in is
/// needed: no agent is started, so this runs in CI.
/// </para>
/// <para>
/// <b>This is the test that was missing.</b> <c>QuestionGrantTests</c> asserted what the tool
/// <em>types</em> declare, and a hub that served all five tools at the question door satisfied every
/// one of those assertions — because <c>AddMcpServer().WithTools&lt;A&gt;().WithTools&lt;B&gt;()</c>
/// builds one catalogue and <c>MapMcp</c> serves that same one at every pattern. What a type declares
/// and what a route serves are two claims, and GUARD-005 is about the second.
/// </para>
/// </remarks>
[Trait("level", "contract")]
[Trait("req", "GUARD-005")]
public sealed class WikiToolDoorTests
{
    [Fact]
    public async Task QuestionDoor_ServesTheTwoReadToolsAndNothingElse()
    {
        await using var hub = await RealRun.StartAsync(TestContext.Current.CancellationToken);

        // Equality, never containment: the whole of GUARD-005 is that nothing else is there. A run
        // served anything beyond these two ends failed before its first model call (GUARD-001), and
        // what is not in this session's catalogue cannot be reached by any name.
        Assert.Equal(
            ["list_pages", "read_page"],
            await ToolsAtAsync(hub, ToolGrant.Questions));
    }

    [Fact]
    [Trait("req", "GUARD-002")]
    public async Task RunDoor_ServesTheFiveToolsAnIngestRunIsGranted()
    {
        await using var hub = await RealRun.StartAsync(TestContext.Current.CancellationToken);

        // The other door, unchanged. Asserted beside GUARD-005's two so that a change narrowing both at
        // once could not pass as a change that narrowed one.
        Assert.Equal(
            ToolGrant.ForIngest.Order(StringComparer.Ordinal),
            await ToolsAtAsync(hub, ToolGrant.Runs));
    }

    /// <summary>
    /// The tool names one door serves, at a run identifier that names no run: what a door serves is
    /// the door's, and a session is not a run.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ToolsAtAsync(RealRun hub, string door)
    {
        var token = TestContext.Current.CancellationToken;

        await using var session = await McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(hub.Address, $"/mcp/{door}/{Guid.NewGuid()}"),
                }),
            cancellationToken: token);

        var served = await session.ListToolsAsync(cancellationToken: token);

        return [.. served.Select(tool => tool.Name).Order(StringComparer.Ordinal)];
    }
}

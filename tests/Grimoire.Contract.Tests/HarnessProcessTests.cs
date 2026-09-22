using System.Text.Json.Nodes;
using Grimoire.Agent;
using Grimoire.Agent.Adapters;

namespace Grimoire.Contract.Tests;

/// <summary>
/// The agent adapter against the real <c>claude</c> CLI — the external thing at this port
/// (Constitution III.4). The reported tool surface and the served model are the CLI's own answers,
/// and whether our deny-by-default configuration and our pinned id actually take hold is exactly
/// what these read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three tests, which is the plan's budget for this suite.</b> Each one costs a real run on the
/// owner's subscription, so the first takes a single run and reads everything that one run can
/// answer rather than spending four runs on four assertions.
/// </para>
/// <para>
/// Marked <c>requires=signin</c>: they need the owner's subscription sign-in, which CI does not
/// have, so CI excludes them and they are run locally before the PR (research.md R-09). That is
/// the Complexity Tracking entry in plan.md.
/// </para>
/// </remarks>
[Trait("level", "contract")]
[Trait("requires", "signin")]
public sealed class HarnessProcessTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("req", "GUARD-001")]
    [Trait("req", "GUARD-002")]
    [Trait("req", "WIKI-002")]
    public async Task Run_ReachesTheWikiAndNothingElse()
    {
        await using var run = await RealRun.StartAsync(TestContext.Current.CancellationToken);
        var escaped = Path.Combine(run.Root, "cwd", "escaped.txt");

        var transcript = await run.TranscriptAsync(
            run.Dispatch(
                "Do two things, in order. First, use the write_page tool once to create a page at "
                + "notes/hello.md whose frontmatter has `type: Note` and whose body is the word hello. "
                + $"Second, run the shell command: echo hello > {escaped}. "
                + "If you cannot do the second, say so and stop."),
            Patience);

        var init = Single(transcript, "system");
        var tools = init["tools"]!.AsArray().Select(t => t!.GetValue<string>()).ToArray();

        // Equality, not containment: with --tools "" every built-in tool is gone, so the whole
        // surface is what the hub serves, each granted name under the CLI's own prefix.
        Assert.Equal(
            run.Grant.ToolNames.Select(n => AgentTranscript.McpPrefix + n).Order(StringComparer.Ordinal),
            tools.Order(StringComparer.Ordinal));
        Assert.True(AgentTranscript.SurfaceIsTheGrant(run.Grant, tools));

        var wiki = init["mcp_servers"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == "wiki")!;
        Assert.Equal("connected", wiki["status"]!.GetValue<string>());

        // No API key is in the child's environment, so this is the owner's subscription serving
        // the id the run asked for rather than an alias or a default (DEC-001, research.md R-11).
        Assert.Contains(RealRun.PinnedModel, Single(transcript, "result")["modelUsage"]!.AsObject().Select(e => e.Key));

        // A granted tool wrote, and the hub stamped it on the way through.
        var page = Path.Combine(run.WikiRoot, "notes", "hello.md");
        Assert.True(File.Exists(page), $"no page at {page}");
        Assert.Contains(
            "generated:",
            await File.ReadAllTextAsync(page, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        // The shell tool does not exist in the run, so there was nothing to call and nothing to
        // deny: no tool call outside the grant, and nothing changed outside the wiki.
        Assert.DoesNotContain(
            transcript.SelectMany(ToolNamesIn),
            n => !n.StartsWith(AgentTranscript.McpPrefix, StringComparison.Ordinal));
        Assert.False(File.Exists(escaped), "the run wrote outside the wiki");
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public async Task Nudge_ContinuesTheSameRun_AfterTheAgentStops()
    {
        await using var run = await RealRun.StartAsync(TestContext.Current.CancellationToken);
        var dispatch = run.Dispatch("Say the word one, then stop.");

        var stops = 0;
        var reportedIn = false;
        var finished = new TaskCompletionSource();

        var report = new RunReport(
            AgentReportedIn: _ => reportedIn = true,
            CostSoFar: (_, _) => { },
            AgentStopped: async (_, _) =>
            {
                if (Interlocked.Increment(ref stops) == 1)
                {
                    // A further user message on the same stdin: the agent carries on in the same
                    // session, inside the same ceilings, rather than a new run starting.
                    await run.Harness.NudgeAsync(dispatch.RunId, CancellationToken.None);
                }
                else
                {
                    finished.TrySetResult();
                }
            },
            RunEnded: (_, _) => finished.TrySetResult());

        await run.Harness.DispatchAsync(dispatch, report, TestContext.Current.CancellationToken);
        await finished.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.True(reportedIn);
        Assert.True(stops >= 2, $"the agent stopped {stops} time(s); the nudge did not continue the run");
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task Interrupt_EndsARunInFlight()
    {
        await using var run = await RealRun.StartAsync(TestContext.Current.CancellationToken);
        var dispatch = run.Dispatch("Count slowly from one to five hundred, one number per line.");

        var abnormal = false;
        Task? stopSent = null;
        var finished = new TaskCompletionSource();

        var report = new RunReport(
            AgentReportedIn: _ => { },

            // The first tokens mean a model call is under way, which is the moment a stop has to
            // reach: nothing can prevent the next call without ending the one in flight (R-04).
            CostSoFar: (_, _) => stopSent ??= run.Harness.StopAsync(dispatch.RunId, CancellationToken.None),
            AgentStopped: (_, endedAbnormally) =>
            {
                abnormal = endedAbnormally;
                finished.TrySetResult();
                return Task.CompletedTask;
            },
            RunEnded: (_, _) => finished.TrySetResult());

        await run.Harness.DispatchAsync(dispatch, report, TestContext.Current.CancellationToken);
        await finished.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await (stopSent ?? Task.CompletedTask);

        // The interrupt ends the turn rather than letting it finish, and the run says so about
        // itself. Whatever it had already written stays in the wiki (WIKI-003).
        Assert.True(abnormal, "the run was interrupted but did not report an abnormal ending");
    }

    private static JsonObject Single(IReadOnlyList<JsonObject> transcript, string type) =>
        transcript.First(m => m["type"]?.GetValue<string>() == type);

    private static IEnumerable<string> ToolNamesIn(JsonObject message) =>
        message["message"]?["content"] is JsonArray blocks
            ? blocks.OfType<JsonObject>()
                .Where(b => b["type"]?.GetValue<string>() == "tool_use")
                .Select(b => b["name"]?.GetValue<string>() ?? string.Empty)
            : [];
}

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
/// <b>Four tests, which is this suite's budget.</b> Each one costs a real run on the owner's
/// subscription, so the first takes a single run and reads everything that one run can answer
/// rather than spending four runs on four assertions. The fourth was added with the weighted cost
/// ceiling: <c>costUSD</c> exists only on a real run, so nothing below this level can hold the four
/// weights to what the CLI actually bills (DEC-015, DEC-021).
/// </para>
/// <para>
/// Each marked <c>requires=signin</c> on the method, not the class: they need the owner's
/// subscription sign-in, which CI does not have, so CI excludes them and they are run locally before
/// the PR (research.md R-09). A fifth such test is one <c>trace-check</c> fails on (DEC-021), and on
/// the method a new test in this class does not inherit the mark unseen. That is the Complexity
/// Tracking entry in plan.md.
/// </para>
/// </remarks>
[Trait("level", "contract")]
public sealed class HarnessProcessTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("req", "GUARD-001")]
    [Trait("req", "GUARD-002")]
    [Trait("req", "WIKI-002")]
    [Trait("requires", "signin")]
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
    [Trait("requires", "signin")]
    public async Task Nudge_ContinuesTheSameRun_AfterTheAgentStops()
    {
        await using var run = await RealRun.StartAsync(TestContext.Current.CancellationToken);
        var dispatch = run.Dispatch("Say the word one, then stop.");

        var stops = 0;
        var reportedIn = false;
        var finished = new TaskCompletionSource();

        var report = new RunReport(
            AgentProcessIs: (_, _) => { },
            AgentReportedIn: _ => reportedIn = true,
            CostSoFar: (_, _) => { },
            AgentExited: (_, _) => { },
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
            RunEnded: (_, _, _) => finished.TrySetResult(),
            MomentHappened: (_, _) => { });

        await run.Harness.DispatchAsync(dispatch, report, TestContext.Current.CancellationToken);
        await finished.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.True(reportedIn);
        Assert.True(stops >= 2, $"the agent stopped {stops} time(s); the nudge did not continue the run");
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    [Trait("requires", "signin")]
    public async Task Interrupt_EndsARunInFlight()
    {
        await using var run = await RealRun.StartAsync(TestContext.Current.CancellationToken);
        var dispatch = run.Dispatch("Count slowly from one to five hundred, one number per line.");

        var abnormal = false;
        Task? stopSent = null;
        var finished = new TaskCompletionSource();

        var report = new RunReport(
            AgentProcessIs: (_, _) => { },
            AgentReportedIn: _ => { },

            // The first tokens mean a model call is under way, which is the moment a stop has to
            // reach: nothing can prevent the next call without ending the one in flight (R-04).
            CostSoFar: (_, _) => stopSent ??= run.Harness.StopAsync(dispatch.RunId, CancellationToken.None),
            AgentExited: (_, _) => { },
            AgentStopped: (_, endedAbnormally) =>
            {
                abnormal = endedAbnormally;
                finished.TrySetResult();
                return Task.CompletedTask;
            },
            RunEnded: (_, _, _) => finished.TrySetResult(),
            MomentHappened: (_, _) => { });

        await run.Harness.DispatchAsync(dispatch, report, TestContext.Current.CancellationToken);
        await finished.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await (stopSent ?? Task.CompletedTask);

        // The interrupt ends the turn rather than letting it finish, and the run says so about
        // itself. Whatever it had already written stays in the wiki (WIKI-003).
        Assert.True(abnormal, "the run was interrupted but did not report an abnormal ending");
    }

    /// <summary>
    /// What one million input tokens of <see cref="RealRun.PinnedModel"/> cost, in US dollars, as
    /// Anthropic's price list states it.
    /// </summary>
    /// <remarks>
    /// <b>The only absolute price in this repository, and it lives here and nowhere else.</b> The
    /// product code counts input-token equivalents and never currency (DEC-015); this test is what
    /// holds the four weights those equivalents are made of to the CLI's own <c>costUSD</c>, and it
    /// cannot do that without one price to multiply by. A price that moves breaks this test and
    /// nothing else.
    /// </remarks>
    private const decimal InputPricePerMillionTokens = 1.00m;

    /// <summary>
    /// How far the two figures may differ. The weighting divides by ten and drops what is left, so
    /// the equivalents can be a tenth short of the exact figure; a tenth of an equivalent is a ten
    /// millionth of a dollar, and this allows ten of those.
    /// </summary>
    private const decimal Tolerance = 0.000001m;

    [Fact]
    [Trait("req", "GUARD-004")]
    [Trait("requires", "signin")]
    public async Task Cost_WeighsAModelUsageAsTheCliBillsIt()
    {
        // What the ceiling counts is input-token equivalents: the four token classes weighted by
        // the ratios Anthropic bills them at (1, 5, a tenth, 2). Multiplied by one model's input
        // price, those equivalents are that run's money — so the CLI's own costUSD is a reading of
        // the same four weights, and this is where they are checked against it. No other test can:
        // costUSD only exists on a real run, and it is why this fourth sign-in test exists at all
        // (DEC-015, DEC-021).
        await using var run = await RealRun.StartAsync(TestContext.Current.CancellationToken);

        var transcript = await run.TranscriptAsync(run.Dispatch("Say hello and stop."), Patience);
        var reported = Single(transcript, "result")["modelUsage"]!.AsObject();

        // One price is multiplied in below, so every entry has to be the model that price belongs
        // to. A background call on another model would need its own price and would make this a
        // price table, which is exactly what the product code does without (DEC-015).
        Assert.All(reported, model => Assert.Equal(RealRun.PinnedModel, model.Key));

        var spent = Ceilings.Sum(reported.Select(model => new ModelTokens(
            Count(model.Value!, "inputTokens"),
            Count(model.Value!, "outputTokens"),
            Count(model.Value!, "cacheReadInputTokens"),
            Count(model.Value!, "cacheCreationInputTokens"))));

        var billed = reported.Sum(model => model.Value!["costUSD"]!.GetValue<decimal>());
        var weighed = Ceilings.CostOf(spent) * InputPricePerMillionTokens / 1_000_000m;

        Assert.True(
            Math.Abs(billed - weighed) <= Tolerance,
            $"the CLI billed {billed} for {spent}, and the four weights make that {weighed}");
    }

    private static long Count(JsonNode model, string field) => model[field]?.GetValue<long>() ?? 0;

    private static JsonObject Single(IReadOnlyList<JsonObject> transcript, string type) =>
        transcript.First(m => m["type"]?.GetValue<string>() == type);

    private static IEnumerable<string> ToolNamesIn(JsonObject message) =>
        message["message"]?["content"] is JsonArray blocks
            ? blocks.OfType<JsonObject>()
                .Where(b => b["type"]?.GetValue<string>() == "tool_use")
                .Select(b => b["name"]?.GetValue<string>() ?? string.Empty)
            : [];
}

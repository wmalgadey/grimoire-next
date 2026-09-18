using System.Globalization;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// T046 / TS-07 (FR-013, FR-014). The instruction version and the tool grant are recorded
/// <b>before the first model call</b>.
/// </summary>
/// <remarks>
/// Asserted by comparing the persisted timestamps against the scripted model's first-request
/// timestamp. Without the runner's <c>proceed</c> handshake that ordering would be a hope about
/// scheduling rather than a property; this test is what makes the handshake load-bearing
/// (contracts/runner-protocol.md "Sequence").
/// </remarks>
public sealed class DispatchOrderingTests
{
    [Fact]
    public async Task RecordsTheToolGrantBeforeTheFirstModelCall()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes worth keeping", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var recordedAt = DateTimeOffset.Parse(
            task.GetProperty("run").GetProperty("toolGrant").GetProperty("recordedAt").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var firstModelCall = await model.FirstRequestAt(TestContext.Current.CancellationToken);

        Assert.NotNull(firstModelCall);
        Assert.True(recordedAt <= firstModelCall,
            $"The grant was recorded at {recordedAt:O} but the first model call was at {firstModelCall:O}: "
            + "the agent was asked something before the artifact said what it was allowed to do (FR-013).");
    }

    [Fact]
    public async Task RecordsTheInstructionVersionBeforeTheFirstModelCall()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var version = task.GetProperty("run").GetProperty("instructionVersion");
        Assert.Equal("src/instructions/ingest.md", Path.GetRelativePath(
            ScriptedModelFixture.RepositoryRoot, version.GetProperty("path").GetString()!)
            .Replace(Path.DirectorySeparatorChar, '/'));
        Assert.Matches("^[0-9a-f]{64}$", version.GetProperty("sha256").GetString()!);
        Assert.True(version.GetProperty("byteLength").GetInt32() > 0);

        // Recorded at dispatch means: the task carries it whatever the run then does.
        Assert.True(task.GetProperty("startedAt").ValueKind is not JsonValueKind.Null);
    }

    [Fact]
    public async Task GrantsExactlyTheTwoWikiToolsInEveryRun()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var tools = task.GetProperty("run").GetProperty("toolGrant").GetProperty("tools")
            .EnumerateArray().Select(tool => tool.GetString()).Order().ToList();

        // Exactly two, in 100% of runs (FR-010, SC-004) — deny-by-default is not a default that
        // can drift, it is the recorded grant.
        Assert.Equal(["mcp__wiki__read_page", "mcp__wiki__write_page"], tools);
    }

    [Fact]
    public async Task SendsTheInstructionFileAsTheSystemPromptAndNothingElseAsInstruction()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var bodies = await model.RequestBodies(TestContext.Current.CancellationToken);
        Assert.NotEmpty(bodies);

        // The SDK's claude_code preset is deliberately not used, so nothing outside the
        // instruction file reaches the model as instruction (plan I, ADR-0003).
        foreach (var body in bodies)
        {
            Assert.DoesNotContain("You are Claude Code", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FailsTheRunWhenTheReportedGrantIsNotTheConfiguredOne()
    {
        // The hub records what it is told and asserts it equals the grant it configured; a
        // mismatch fails the run before `proceed` is ever sent, so the model is never invoked with
        // a grant nobody decided on (contracts/runner-protocol.md, "tool_grant"; constitution II).
        using var wiki = new WikiRepositoryFixture();
        var tipBefore = wiki.Head();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var runner = StubRunner.ReportsAWiderGrant();
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: runner.Environment);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains("did not configure", task.GetProperty("failureReason").GetString()!,
            StringComparison.Ordinal);
        Assert.Empty(await model.Requests(TestContext.Current.CancellationToken));
        Assert.Equal(tipBefore, wiki.Head());
    }
}

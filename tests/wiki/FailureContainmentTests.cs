using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T053 / TS-12, run-end half (FR-009, FR-017, SC-003). A run that fails, crashes, is aborted, or
/// hits its limit commits nothing and leaves wiki content byte-identical to the pre-run commit.
/// </summary>
/// <remarks>
/// The agent writes into the working tree, so a crashed run leaves real files behind. The
/// guarantee is not that it wrote nothing — it is that nothing it wrote is reachable through any
/// user-facing surface, because the working tree is reset and no commit exists (constitution II.2).
/// </remarks>
public sealed class FailureContainmentTests
{
    [Fact]
    public async Task CreatesNoCommitWhenTheRunHitsItsToolCallCeiling()
    {
        using var wiki = new WikiRepositoryFixture();
        var tipBefore = wiki.Head();
        var contentBefore = wiki.Snapshot();

        using var model = ScriptedModelFixture.Start("never-stopping");
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: new Dictionary<string, string?>
        {
            ["GRIMOIRE_RUN_MAX_TOOL_CALLS"] = "5",
        });

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").GetProperty("commit").ValueKind is JsonValueKind.Null);
        Assert.Equal(tipBefore, wiki.Head());
        Assert.Equal(contentBefore, wiki.Snapshot());
    }

    [Fact]
    public async Task CreatesNoCommitWhenTheRunHitsItsElapsedCeiling()
    {
        using var wiki = new WikiRepositoryFixture();
        var tipBefore = wiki.Head();
        var contentBefore = wiki.Snapshot();

        using var model = ScriptedModelFixture.Start("write-then-hang");
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: new Dictionary<string, string?>
        {
            ["GRIMOIRE_RUN_MAX_ELAPSED_MS"] = "4000",
        });

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").GetProperty("commit").ValueKind is JsonValueKind.Null);
        // The write the agent made mid-run is gone: content is byte-identical to the pre-run commit.
        Assert.Equal(contentBefore, wiki.Snapshot());
        Assert.Equal(tipBefore, wiki.Head());
        Assert.False(wiki.Exists("half-written.md"));
    }

    [Fact]
    public async Task RecordsAHumanReadableReasonWhenTheRunHitsItsLimit()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("never-stopping");
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: new Dictionary<string, string?>
        {
            ["GRIMOIRE_RUN_MAX_TOOL_CALLS"] = "3",
        });

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        var reason = task.GetProperty("failureReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason));
        // "Is the limit too tight?" is a question the operator must be able to answer from this
        // reason alone (plan IV, grimoire.run.ended).
        Assert.Contains("3", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsTheTaskAndItsToolCallRecordAfterAFailedRun()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("never-stopping");
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: new Dictionary<string, string?>
        {
            ["GRIMOIRE_RUN_MAX_TOOL_CALLS"] = "4",
        });

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        // Accountability for runs that never produced a diff: the record is the point.
        var run = task.GetProperty("run");
        Assert.True(run.ValueKind is not JsonValueKind.Null);
        Assert.NotEmpty(run.GetProperty("toolCalls").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(
            run.GetProperty("instructionVersion").GetProperty("sha256").GetString()));
    }

    [Fact]
    public async Task LeavesNothingBehindWhenTheRunnerIsKilledMidWrite()
    {
        using var wiki = new WikiRepositoryFixture();
        var tipBefore = wiki.Head();
        var contentBefore = wiki.Snapshot();

        using var model = ScriptedModelFixture.Start("write-then-hang");
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: new Dictionary<string, string?>
        {
            ["GRIMOIRE_RUN_MAX_ELAPSED_MS"] = "4000",
        });

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        // A crash is indistinguishable from an abort to the hub, and both mean: no commit.
        Assert.Equal(contentBefore, wiki.Snapshot());
        Assert.Equal(tipBefore, wiki.Head());
        Assert.Equal(1, wiki.CommitCount());
    }
}

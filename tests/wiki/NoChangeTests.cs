using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T054 (FR-016, SC-011). "The wiki already says this" is a legitimate outcome, and the task has
/// to say so <b>explicitly</b> rather than presenting an empty diff that reads as breakage.
/// </summary>
/// <remarks>
/// Whether to leave the wiki unchanged is judgment and lives in the instruction file. What the
/// system does about it — end <c>completed</c>, commit nothing, and state the fact — is control.
/// </remarks>
public sealed class NoChangeTests
{
    [Fact]
    public async Task EndsTheTaskCompletedWhenTheRunChangedNothing()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-only");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("something the wiki already covers", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Completed, not failed: the agent did its job and decided there was nothing to add.
        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.Equal("completed", task.GetProperty("run").GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("failureReason").ValueKind);
    }

    [Fact]
    public async Task CreatesNoCommitForARunThatChangedNothing()
    {
        using var wiki = new WikiRepositoryFixture();
        var before = wiki.CommitCount();
        var tip = wiki.Head();
        using var model = ScriptedModelFixture.Start("read-only");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // No commit at all, not an empty one: history records what changed, and nothing did.
        Assert.Equal(before, wiki.CommitCount());
        Assert.Equal(tip, wiki.Head());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("run").GetProperty("commit").ValueKind);
    }

    [Fact]
    public async Task StatesChangedNothingExplicitlySoANoOpDoesNotReadAsBroken()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-only");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.True(task.GetProperty("run").GetProperty("changedNothing").GetBoolean(),
            "A completed run with no commit must say so explicitly (FR-016, SC-011).");
    }

    [Fact]
    public async Task StillShowsWhatTheRunLookedAtSoANoOpCanBeToldFromABrokenRun()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-only");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // The tool-call record is what distinguishes "read the wiki and decided against writing"
        // from "did nothing at all" (SC-011).
        var calls = task.GetProperty("run").GetProperty("toolCalls").EnumerateArray().ToList();
        Assert.Single(calls);
        Assert.Equal("mcp__wiki__read_page", calls[0].GetProperty("tool").GetString());
        Assert.Equal("ok", calls[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task OffersNoRevertForATaskThatProducedNoCommit()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-only");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var eligibility = task.GetProperty("revertEligibility");
        Assert.False(eligibility.GetProperty("eligible").GetBoolean());
        Assert.Equal("no-commit", eligibility.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task TreatsARunThatTouchedNothingAtAllTheSameWay()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").GetProperty("changedNothing").GetBoolean());
        Assert.Empty(task.GetProperty("run").GetProperty("toolCalls").EnumerateArray());
    }
}

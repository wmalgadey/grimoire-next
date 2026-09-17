using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T080 / TS-11, revert half (FR-025, SC-005). Revert restores the content the run changed as a
/// <b>new</b> commit: the run's own commit stays in history, and nothing is rewritten or discarded.
/// </summary>
/// <remarks>
/// Undo that rewrote history would make the task artifact a lie — the commit it names would no
/// longer exist, and the record of what the agent did would be unreachable from the wiki it
/// describes. Restoring forward is what lets an ingest stay inspectable after it has been undone,
/// which is the whole of "revertible" in constitution II.1.
/// </remarks>
public sealed class RevertTests
{
    [Fact]
    public async Task RestoresContentByteIdenticalToTheCommitBeforeTheRun()
    {
        using var wiki = new WikiRepositoryFixture();
        var contentBefore = wiki.Snapshot();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes worth keeping", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // The premise: without a change there is nothing for this test to be about.
        Assert.NotEqual(contentBefore, wiki.Snapshot());

        using var reverted = await hub.Revert(id, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reverted.StatusCode);

        Assert.Equal(contentBefore, wiki.Snapshot());
    }

    [Fact]
    public async Task RestoresAsANewCommitAndLeavesTheRunsOwnCommitInHistory()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        var runCommit = task.GetProperty("run").GetProperty("commit").GetProperty("sha").GetString()!;
        var countAfterRun = wiki.CommitCount();

        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Forward, not backward: one commit more, not one fewer.
        Assert.Equal(countAfterRun + 1, wiki.CommitCount());
        Assert.Contains(runCommit, wiki.CommitShas());
        Assert.NotEqual(runCommit, wiki.Head());
    }

    [Fact]
    public async Task RecordsTheRestoringCommitOnTheTaskAndMarksItReverted()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        var runCommit = task.GetProperty("run").GetProperty("commit").GetProperty("sha").GetString()!;

        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("reverted", body.GetProperty("state").GetString());
        var revert = body.GetProperty("revert");
        Assert.Equal(wiki.Head(), revert.GetProperty("revertCommitSha").GetString());
        Assert.True(revert.TryGetProperty("revertedAt", out _));

        // The run's record is untouched by the undo: what the agent did is still readable
        // afterwards, commit identity and all (FR-023).
        var run = body.GetProperty("run");
        Assert.Equal(runCommit, run.GetProperty("commit").GetProperty("sha").GetString());
        Assert.Equal(2, run.GetProperty("toolCalls").GetArrayLength());
    }

    [Fact]
    public async Task LeavesTheWorkingTreeClean()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Residue left staged or unstaged would be swept into the next run's commit, which is how
        // "exactly one commit per run" (FR-015) stops being true without anything looking broken.
        Assert.True(wiki.IsClean(), "Revert left the working tree dirty.");
    }
}

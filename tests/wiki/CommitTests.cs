using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T052 / TS-11, commit half (FR-015, SC-002). A run that changed content produces <b>exactly
/// one</b> commit — never zero, never two — and its identity is recorded on the task.
/// </summary>
/// <remarks>
/// One commit per run is what makes an ingest a single revertible unit (constitution II.1). Two
/// commits would make "undo this ingest" ambiguous; zero would make the change unreachable.
/// </remarks>
public sealed class CommitTests
{
    [Fact]
    public async Task ProducesExactlyOneCommitForARunThatChangedContent()
    {
        using var wiki = new WikiRepositoryFixture();
        var before = wiki.CommitCount();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes worth keeping", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, wiki.CommitCount());
    }

    [Fact]
    public async Task RecordsTheCommitIdentityAndItsParentOnTheTask()
    {
        using var wiki = new WikiRepositoryFixture();
        var parentBefore = wiki.Head();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var commit = task.GetProperty("run").GetProperty("commit");
        Assert.True(commit.ValueKind is not JsonValueKind.Null, "The run changed content but recorded no commit.");
        Assert.Equal(wiki.Head(), commit.GetProperty("sha").GetString());
        // The parent is the commit a failed run would have left the wiki at, and the content a
        // revert restores (FR-017, FR-025).
        Assert.Equal(parentBefore, commit.GetProperty("parentSha").GetString());
    }

    [Fact]
    public async Task TakesTheCommitMessageFromTheAgentsFinalMessageVerbatim()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Commit-message wording is judgment and lives in the instruction file (research R9).
        Assert.Equal("Add scripted topic page",
            task.GetProperty("run").GetProperty("commit").GetProperty("message").GetString());
    }

    [Fact]
    public async Task PutsEveryPageTheRunTouchedIntoThatOneCommit()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("escalation-8");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // The escalation script writes step-2, -4, -6 and -8; all four land in one commit.
        var diffs = task.GetProperty("run").GetProperty("commit").GetProperty("fileDiffs")
            .EnumerateArray().Select(diff => diff.GetProperty("path").GetString()).Order().ToList();

        Assert.Equal(["step-2.md", "step-4.md", "step-6.md", "step-8.md"], diffs);
        Assert.Equal(2, wiki.CommitCount());
    }

    [Fact]
    public async Task ShowsThePerFileDiffOfThatCommit()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        var diff = task.GetProperty("run").GetProperty("commit").GetProperty("fileDiffs")
            .EnumerateArray().Single();

        Assert.Equal("topics/scripted.md", diff.GetProperty("path").GetString());
        Assert.Equal("added", diff.GetProperty("changeKind").GetString());
        Assert.Contains("# Scripted", diff.GetProperty("patch").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndsTheTaskCompletedWhenTheRunCommitted()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.Equal("completed", task.GetProperty("run").GetProperty("outcome").GetString());
        Assert.False(task.GetProperty("run").GetProperty("changedNothing").GetBoolean());
    }

    [Fact]
    public async Task GivesTwoRunsThatChangedContentTwoSeparateCommits()
    {
        using var wiki = new WikiRepositoryFixture();
        var before = wiki.CommitCount();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var first = await hub.SubmitText("first", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(first, TestContext.Current.CancellationToken);

        // A different page, because two runs writing identical content is a no-change run for the
        // second — which is right, and a different test (FR-016).
        await model.UseScript("write-only", TestContext.Current.CancellationToken);
        var second = await hub.SubmitText("second", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(second, TestContext.Current.CancellationToken);

        // One per run, so each ingest stays its own revertible unit.
        Assert.Equal(before + 2, wiki.CommitCount());
    }

    [Fact]
    public async Task GivesNoSecondCommitToARunThatRewroteTheSameContent()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var first = await hub.SubmitText("first", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(first, TestContext.Current.CancellationToken);
        var afterFirst = wiki.CommitCount();

        var second = await hub.SubmitText("the same source again", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(second, TestContext.Current.CancellationToken);

        // The agent wrote the page's own content back. Nothing changed, so nothing is committed
        // and the task says so rather than showing an empty diff (FR-016, SC-011).
        Assert.Equal(afterFirst, wiki.CommitCount());
        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("run").GetProperty("changedNothing").GetBoolean());
    }
}

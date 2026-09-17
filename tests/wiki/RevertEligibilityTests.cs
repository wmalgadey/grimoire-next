using System.Text.Json;
using Grimoire.Tests.Support;
using Grimoire.Wiki;

namespace Grimoire.Tests.Wiki;

/// <summary>
/// T081 (FR-024, FR-027). Revert is offered <b>exactly when</b> the run produced a commit, that
/// commit is the wiki's current tip, and the task is not already reverted. Otherwise the answer
/// carries a reason, and the reason is one of three named ones.
/// </summary>
/// <remarks>
/// The reason is as much the subject as the verdict. A disabled control says nothing; "superseded
/// by a later wiki commit" says why undo reaches one ingest back and not further, which is the
/// difference between a product that can be reasoned about and one that cannot (FR-027).
/// </remarks>
public sealed class RevertEligibilityTests
{
    private const string Tip = "1111111111111111111111111111111111111111";
    private const string Older = "2222222222222222222222222222222222222222";

    [Fact]
    public void OffersRevertWhenTheRunsCommitIsTheTipAndTheTaskIsNotReverted()
    {
        var verdict = RevertEligibility.For(Tip, alreadyReverted: false, tip: Tip);

        Assert.True(verdict.Eligible);
        Assert.Null(verdict.Reason);
    }

    [Fact]
    public void RefusesWithNoCommitWhenTheRunProducedNone()
    {
        var verdict = RevertEligibility.For(null, alreadyReverted: false, tip: Tip);

        Assert.False(verdict.Eligible);
        Assert.Equal(RevertEligibility.NoCommit, verdict.Reason);
    }

    [Fact]
    public void RefusesWithSupersededWhenSomethingCommittedOnTop()
    {
        var verdict = RevertEligibility.For(Older, alreadyReverted: false, tip: Tip);

        Assert.False(verdict.Eligible);
        Assert.Equal(RevertEligibility.Superseded, verdict.Reason);
    }

    [Fact]
    public void RefusesWithAlreadyRevertedBeforeAnythingElseIsConsidered()
    {
        // Ordering matters: after a revert the task's commit is no longer the tip either, and
        // "superseded" would be the wrong thing to tell someone about their own undo.
        var verdict = RevertEligibility.For(Older, alreadyReverted: true, tip: Tip);

        Assert.False(verdict.Eligible);
        Assert.Equal(RevertEligibility.AlreadyReverted, verdict.Reason);
    }

    [Fact]
    public void NamesOnlyTheThreeReasonsTheContractDeclares()
    {
        string?[] reasons =
        [
            RevertEligibility.For(Tip, false, Tip).Reason,
            RevertEligibility.For(null, false, Tip).Reason,
            RevertEligibility.For(Older, false, Tip).Reason,
            RevertEligibility.For(Tip, true, Tip).Reason,
        ];

        // The enum in contracts/hub-api.openapi.yaml, and nothing else: a fourth reason would
        // reach a surface that has no words for it.
        Assert.All(reasons, reason => Assert.Contains(
            reason, new string?[] { null, "no-commit", "superseded", "already-reverted" }));
    }

    [Fact]
    public async Task ATaskWhoseRunChangedNothingIsRefusedWithNoCommit()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-only");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("something the wiki already covers", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Completed, not failed — and still nothing to undo (FR-016).
        Assert.Equal("completed", task.GetProperty("state").GetString());
        var eligibility = task.GetProperty("revertEligibility");
        Assert.False(eligibility.GetProperty("eligible").GetBoolean());
        Assert.Equal("no-commit", eligibility.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ALaterWikiCommitSupersedesTheIngest()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        // Offered while the ingest's commit is still the tip.
        var offered = await hub.GetTask(id, TestContext.Current.CancellationToken);
        Assert.True(offered.GetProperty("revertEligibility").GetProperty("eligible").GetBoolean());

        wiki.Write("topics/edited-by-hand.md", "# Edited\n");
        wiki.Commit("a later change to the wiki");

        // Undo reaches one ingest back, not further (data-model "Revert eligibility").
        var superseded = await hub.GetTask(id, TestContext.Current.CancellationToken);
        var eligibility = superseded.GetProperty("revertEligibility");
        Assert.False(eligibility.GetProperty("eligible").GetBoolean());
        Assert.Equal("superseded", eligibility.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ARevertedTaskIsRefusedWithAlreadyReverted()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        using var response = await hub.Revert(id, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var task = await hub.GetTask(id, TestContext.Current.CancellationToken);
        var eligibility = task.GetProperty("revertEligibility");
        Assert.False(eligibility.GetProperty("eligible").GetBoolean());
        Assert.Equal("already-reverted", eligibility.GetProperty("reason").GetString());
    }
}

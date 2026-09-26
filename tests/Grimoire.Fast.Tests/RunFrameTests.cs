using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The frame of a run: what the head holds when it begins, what the tail holds when it ends, and
/// which of the seven reasons each ending is recorded as (RUNS-008).
/// </summary>
/// <remarks>
/// Every field comes from domain objects the Fast suite already drives, and both ceilings are reached
/// with <c>FakeTimeProvider</c> rather than waited for — the suite has 15 s in total (DEC-018).
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "RUNS-008")]
public sealed class RunFrameTests
{
    private readonly FastHub hub = new();

    [Fact]
    public async Task Head_HoldsWhatIsKnownWhenTheRunBegins()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        var head = hub.Record.HeadOf(run.Id)!;

        Assert.Equal(run.Id, head.RunId);
        Assert.Equal(submission.Id, head.SubmissionId);
        Assert.Equal(FastHub.Model, head.Model);
        Assert.Equal(run.Grant.ToolNames, head.GrantedTools);
        Assert.Equal(run.Grant.RecordedAt, head.GrantRecordedAt);
        Assert.Equal(Ceilings.Fixed, head.Ceilings);
        Assert.Equal(run.StartedAt, head.StartedAt);
    }

    [Fact]
    public async Task Tail_IsNotThere_WhileTheRunIsUnderWay()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);

        // The head and however many moments have happened, and no tail. That is the only difference
        // between a record of a run under way and one from last month (research.md R-02).
        Assert.NotNull(hub.Record.HeadOf(run.Id));
        Assert.Null(hub.Record.TailOf(run.Id));
    }

    [Fact]
    public async Task Tail_HoldsWhereTheRunStoodAndWhatEachModelSpent()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);

        var spent = new Dictionary<string, ModelTokens>(StringComparer.Ordinal)
        {
            [FastHub.Model] = new(InputTokens: 40_112, OutputTokens: 3_190, 22_016, 7_228),
            ["claude-haiku-4-5-20251001"] = new(InputTokens: 897, OutputTokens: 12, 0, 0),
        };

        hub.Harness.Spend(submission.Id, spent);
        hub.Clock.Advance(TimeSpan.FromMinutes(3));

        await hub.Wiki.AppendLogAsync($"Run {run.Id} wrote a page.\n", TestContext.Current.CancellationToken);
        await hub.Harness.StoppedAsync(submission.Id);

        var tail = hub.Record.TailOf(run.Id)!;

        Assert.Equal(hub.Clock.GetUtcNow(), tail.EndedAt);
        Assert.Equal(RunOutcome.Done, tail.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(3), tail.Elapsed);
        Assert.Equal(Ceilings.CostOf(spent.Values), tail.TokensUsed);

        // Both ceilings travel with the figures, because a figure without the ceiling beside it says
        // nothing about how close the run came to it.
        Assert.Equal(Ceilings.Fixed, tail.Ceilings);

        // And which model spent them, never a single number for all of them (DEC-015).
        Assert.Equal(spent, tail.TokensPerModel);
    }

    [Fact]
    public async Task Tail_SaysTheAgentStoppedWithItsLogEntry_WhenAllThreeAgree()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);
        await hub.Wiki.AppendLogAsync($"Run {run.Id} wrote a page.\n", TestContext.Current.CancellationToken);
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(RunEndedBecause.StoppedWithItsLogEntry, hub.Record.TailOf(run.Id)!.EndedBecause);
    }

    [Fact]
    public async Task Tail_SaysTheAgentStoppedWithoutItsLogEntry_AfterTheOneNudge()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);

        // Told once, and the entry is still missing when it stops again (RUNS-005).
        await hub.Harness.StoppedAsync(submission.Id);
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(RunEndedBecause.StoppedWithoutItsLogEntry, hub.Record.TailOf(run.Id)!.EndedBecause);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task Tail_SaysTheTimeCeiling_WhenTheRunRanOutOfTime()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);

        // The clock is moved rather than waited for: the ceiling is fifteen minutes and the suite has
        // fifteen seconds (DEC-018).
        hub.Clock.Advance(Ceilings.Fixed.Elapsed);

        Assert.Equal(RunEndedBecause.TimeCeiling, hub.Record.TailOf(run.Id)!.EndedBecause);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task Tail_SaysTheCostCeiling_WhenTheRunSpentTooMuch()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Spend(submission.Id, Ceilings.Fixed.Tokens);

        Assert.Equal(RunEndedBecause.CostCeiling, hub.Record.TailOf(run.Id)!.EndedBecause);
    }

    [Fact]
    [Trait("req", "GUARD-001")]
    public async Task Tail_SaysTheToolsWereNotTheGrant_WhenTheAgentReportedAnotherSurface()
    {
        hub.Harness.ReportedSurface = ["mcp__wiki__read_page", "Bash"];

        var submission = await hub.AcceptedAsync();

        // The run ended before its first model call, so there is no run to ask the conductor for: the
        // record is what is left of it, which is the whole point of writing the head at Begin.
        var head = Assert.Single(hub.Record.Of(hub.Store.Load().Single().Run!.Id).OfType<RunFrameHead>());
        var tail = hub.Record.TailOf(head.RunId)!;

        Assert.Equal(RunEndedBecause.ToolsWereNotTheGrant, tail.EndedBecause);
        Assert.Equal(RunOutcome.Failed, tail.Outcome);
        Assert.Empty(hub.Record.MomentsOf(head.RunId));
        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Fact]
    public async Task Tail_SaysTheAgentProcessDied_WhenItExitedNonZero()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);
        await hub.Wiki.AppendLogAsync($"Run {run.Id} wrote a page.\n", TestContext.Current.CancellationToken);

        // A CLI that reports a clean result and then exits non-zero did not do what it said it did.
        hub.Harness.ExitCode = 1;
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(RunEndedBecause.AgentProcessDied, hub.Record.TailOf(run.Id)!.EndedBecause);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Tail_SaysGrimoireStopped_WhenTheHubWentDownWithTheRunUnderWay()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);
        await hub.StopEverythingAsync();

        Assert.Equal(RunEndedBecause.GrimoireStopped, hub.Record.TailOf(run.Id)!.EndedBecause);
    }

    [Fact]
    public void Reasons_AreTheSevenTheSpecNames() =>
        Assert.Equal(
            [
                RunEndedBecause.StoppedWithItsLogEntry,
                RunEndedBecause.StoppedWithoutItsLogEntry,
                RunEndedBecause.TimeCeiling,
                RunEndedBecause.CostCeiling,
                RunEndedBecause.ToolsWereNotTheGrant,
                RunEndedBecause.AgentProcessDied,
                RunEndedBecause.GrimoireStopped,
            ],
            Enum.GetValues<RunEndedBecause>());
}

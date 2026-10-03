using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// How a run ends: the decision table of <c>contracts/agent-cli-protocol.md</c>, whole (RUNS-005).
/// </summary>
[Trait("level", "fast")]
public sealed class RunOutcomeTests
{
    private readonly Run run = NewRun();

    private static Run NewRun() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        FastSuite.Start,
        ToolGrant.Ingest(FastSuite.Clock()),
        Ceilings.Fixed,
        FastHub.Model);

    /// <summary>
    /// A run a question caused: read-only, and <b>not one that is to change the wiki</b>. RUNS-005's
    /// log condition and its one nudge are asked of a run that is, which is what this run is here to
    /// show it is not (RUNS-005, GUARD-005).
    /// </summary>
    private static Run NewQuestionRun() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        FastSuite.Start,
        ToolGrant.Question(FastSuite.Clock()),
        Ceilings.Fixed,
        FastHub.Model,
        RunCause.AQuestion);

    private static AgentStop Stopped(bool logEntry) => new(logEntry, TimeSpan.FromMinutes(1), CostSpent: 10);

    [Fact]
    [Trait("req", "RUNS-005")]
    public void AgentStops_EndsTheRunDone_WithNoLogEntryWhenTheRunChangesNothing()
    {
        var question = NewQuestionRun();

        // A question's run writes nothing in the wiki, so it can never satisfy the log condition: asked
        // of it, that condition would fail every single question. RUNS-005 as this feature rewords it
        // asks for the entry only of a run **that is to change the wiki**, and this is the case that
        // made it change.
        Assert.False(question.IsToChangeTheWiki);
        Assert.Equal(RunDecision.Done, question.AgentStopped(Stopped(logEntry: false)));
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public void AgentStops_SendsNoNudge_WhenTheRunChangesNothing()
    {
        var question = NewQuestionRun();

        question.AgentStopped(Stopped(logEntry: false));

        // **No nudge**, and not merely one rather than two: there is nothing to tell the agent, because
        // no entry was ever asked of it. The nudge exists to give a run that was to write one a second
        // chance, and telling a question's agent its entry is missing would ask it for something it has
        // no tool to do (RUNS-005, GUARD-005).
        Assert.False(question.LogEntryNudged);
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public void Exit_EndsTheRunDone_WhenTheRunChangedNothingAndStoppedInsideBothCeilings()
    {
        var question = NewQuestionRun();

        question.AgentStopped(Stopped(logEntry: false));

        // The verdict where a run actually ends. Stopping on its own inside both ceilings with a clean
        // exit is the whole of done for a run that was to change nothing (RUNS-005). Nothing is said
        // about the reason it carries: for such a run that is a placeholder (tasks.md, Later).
        var ending = question.Exited(exitCode: 0, TimeSpan.FromMinutes(1));

        Assert.Equal(RunOutcome.Done, ending.Outcome);
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public async Task AgentStops_AsksTheWikiForNothing_WhenTheRunChangesNothing()
    {
        var hub = new FastHub();
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        await hub.Harness.StoppedAsync(question.Id);
        hub.Harness.Exit(question.Id, exitCode: 0);

        // RUNS-005's last clause is unchanged — Grimoire reads nothing else in the wiki to decide this —
        // and for a question's run it reads **nothing at all**: not `log.md`, which is the one file the
        // decision used to turn on. The in-memory store is what can be asked what it was asked; nothing
        // below it can (Constitution III.6).
        Assert.Empty(hub.Wiki.Asked);

        // And it ended done, having written nothing.
        Assert.Equal(QuestionState.Answered, question.State);
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public void AgentStops_EndsTheRunDone_WithTheLogEntryPresent() =>
        Assert.Equal(RunDecision.Done, run.AgentStopped(Stopped(logEntry: true)));

    [Fact]
    [Trait("req", "RUNS-005")]
    public void AgentStops_NudgesOnce_WhenLogEntryMissing()
    {
        Assert.Equal(RunDecision.Nudge, run.AgentStopped(Stopped(logEntry: false)));
        Assert.True(run.LogEntryNudged);
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public void AgentStops_EndsTheRunDone_AfterTheNudgeBringsTheEntry()
    {
        run.AgentStopped(Stopped(logEntry: false));

        Assert.Equal(RunDecision.Done, run.AgentStopped(Stopped(logEntry: true)));
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public void AgentStops_EndsTheRunFailed_WhenTheEntryIsStillMissingAfterTheNudge()
    {
        run.AgentStopped(Stopped(logEntry: false));

        Assert.Equal(RunDecision.Failed, run.AgentStopped(Stopped(logEntry: false)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("req", "GUARD-004")]
    [Trait("req", "RUNS-005")]
    public void AgentStops_EndsTheRunFailed_WhenTheElapsedCeilingIsReached(bool logEntry) =>
        Assert.Equal(
            RunDecision.Failed,
            run.AgentStopped(new AgentStop(logEntry, Ceilings.Fixed.Elapsed, CostSpent: 0)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("req", "GUARD-004")]
    [Trait("req", "RUNS-005")]
    public void AgentStops_EndsTheRunFailed_WhenTheCostCeilingIsReached(bool logEntry) =>
        Assert.Equal(
            RunDecision.Failed,
            run.AgentStopped(new AgentStop(logEntry, TimeSpan.Zero, Ceilings.Fixed.Cost)));

    [Fact]
    [Trait("req", "RUNS-005")]
    public void AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord() =>
        Assert.Equal(
            RunDecision.Failed,
            run.AgentStopped(new AgentStop(LogEntryPresent: true, TimeSpan.Zero, CostSpent: 0, EndedAbnormally: true)));

    [Fact]
    [Trait("req", "RUNS-010")]
    public void AgentStops_RecordsWhatTheRunSpent()
    {
        run.AgentStopped(new AgentStop(LogEntryPresent: true, TimeSpan.Zero, CostSpent: 4_211));

        Assert.Equal(4_211, run.CostSpent);
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    [Trait("req", "GUARD-004")]
    [Trait("req", "RUNS-005")]
    public void Exit_SaysTheTimeCeiling_WhenTheRunStandsExactlyOnIt()
    {
        // Exactly on the ceiling is on it: `Ceilings.ReachedBy` says so, and the reason recorded has
        // to agree — a run that ran out of time recorded as having run out of money would name the
        // wrong one of RUNS-008's seven.
        var ending = run.Exited(exitCode: 0, Ceilings.Fixed.Elapsed);

        Assert.Equal(RunOutcome.Failed, ending.Outcome);
        Assert.Equal(RunEndedBecause.TimeCeiling, ending.Because);
    }

    [Fact]
    [Trait("req", "RUNS-005")]
    public void Log_NamesTheRun_WhenItHoldsTheIdentifierAsPlainText() =>
        Assert.True(run.IsNamedIn($"## 2026-09-20\n\nRun {run.Id} added one page and linked it.\n"));

    [Fact]
    [Trait("req", "RUNS-005")]
    public void Log_DoesNotNameTheRun_WithoutTheIdentifierInIt() =>
        Assert.False(run.IsNamedIn($"## 2026-09-20\n\nRun {Guid.NewGuid()} added one page.\n"));

    [Fact]
    [Trait("req", "RUNS-005")]
    public void Log_DoesNotNameTheRun_WithoutALogAtAll() =>
        Assert.False(run.IsNamedIn(null));

    [Fact]
    [Trait("req", "RUNS-005")]
    public void Log_NamesTheRun_WithNoFormatImposedOnTheEntry()
    {
        // Nothing about the entry is parsed but the identifier: not its shape, not its prose, not
        // which lines belong to which entry. The log stays a document a person reads.
        Assert.True(run.IsNamedIn(run.Id.ToString()));
        Assert.True(run.IsNamedIn($"...rambling prose, no structure at all, {run.Id.ToString().ToUpperInvariant()}..."));
    }
}

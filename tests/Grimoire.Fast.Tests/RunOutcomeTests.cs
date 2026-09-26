using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// How a run ends: the decision table of <c>contracts/agent-cli-protocol.md</c>, whole (RUNS-005).
/// </summary>
[Trait("level", "fast")]
[Trait("req", "RUNS-005")]
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

    private static AgentStop Stopped(bool logEntry) => new(logEntry, TimeSpan.FromMinutes(1), TokensUsed: 10);

    [Fact]
    public void AgentStops_EndsTheRunDone_WithTheLogEntryPresent() =>
        Assert.Equal(RunDecision.Done, run.AgentStopped(Stopped(logEntry: true)));

    [Fact]
    public void AgentStops_NudgesOnce_WhenLogEntryMissing()
    {
        Assert.Equal(RunDecision.Nudge, run.AgentStopped(Stopped(logEntry: false)));
        Assert.True(run.LogEntryNudged);
    }

    [Fact]
    public void AgentStops_EndsTheRunDone_AfterTheNudgeBringsTheEntry()
    {
        run.AgentStopped(Stopped(logEntry: false));

        Assert.Equal(RunDecision.Done, run.AgentStopped(Stopped(logEntry: true)));
    }

    [Fact]
    public void AgentStops_EndsTheRunFailed_WhenTheEntryIsStillMissingAfterTheNudge()
    {
        run.AgentStopped(Stopped(logEntry: false));

        Assert.Equal(RunDecision.Failed, run.AgentStopped(Stopped(logEntry: false)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("req", "GUARD-004")]
    public void AgentStops_EndsTheRunFailed_WhenTheElapsedCeilingIsReached(bool logEntry) =>
        Assert.Equal(
            RunDecision.Failed,
            run.AgentStopped(new AgentStop(logEntry, Ceilings.Fixed.Elapsed, TokensUsed: 0)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("req", "GUARD-004")]
    public void AgentStops_EndsTheRunFailed_WhenTheTokenCeilingIsReached(bool logEntry) =>
        Assert.Equal(
            RunDecision.Failed,
            run.AgentStopped(new AgentStop(logEntry, TimeSpan.Zero, Ceilings.Fixed.Tokens)));

    [Fact]
    public void AgentStops_EndsTheRunFailed_WhenItDidNotStopOfItsOwnAccord() =>
        Assert.Equal(
            RunDecision.Failed,
            run.AgentStopped(new AgentStop(LogEntryPresent: true, TimeSpan.Zero, TokensUsed: 0, EndedAbnormally: true)));

    [Fact]
    public void AgentStops_RecordsWhatTheRunSpent()
    {
        run.AgentStopped(new AgentStop(LogEntryPresent: true, TimeSpan.Zero, TokensUsed: 4_211));

        Assert.Equal(4_211, run.TokensUsed);
    }

    [Fact]
    public void Log_NamesTheRun_WhenItHoldsTheIdentifierAsPlainText() =>
        Assert.True(run.IsNamedIn($"## 2026-09-20\n\nRun {run.Id} added one page and linked it.\n"));

    [Fact]
    public void Log_DoesNotNameTheRun_WithoutTheIdentifierInIt() =>
        Assert.False(run.IsNamedIn($"## 2026-09-20\n\nRun {Guid.NewGuid()} added one page.\n"));

    [Fact]
    public void Log_DoesNotNameTheRun_WithoutALogAtAll() =>
        Assert.False(run.IsNamedIn(null));

    [Fact]
    public void Log_NamesTheRun_WithNoFormatImposedOnTheEntry()
    {
        // Nothing about the entry is parsed but the identifier: not its shape, not its prose, not
        // which lines belong to which entry. The log stays a document a person reads.
        Assert.True(run.IsNamedIn(run.Id.ToString()));
        Assert.True(run.IsNamedIn($"...rambling prose, no structure at all, {run.Id.ToString().ToUpperInvariant()}..."));
    }
}

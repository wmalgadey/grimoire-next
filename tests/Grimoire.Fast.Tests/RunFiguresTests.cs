using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The two figures a run carries — the tokens it has spent and the tool calls it has made — kept
/// current while it runs, final once it has ended, and surviving a stop (RUNS-010).
/// </summary>
[Trait("level", "fast")]
[Trait("req", "RUNS-010")]
public sealed class RunFiguresTests
{
    private readonly FastHub hub = new();

    [Fact]
    public async Task Figures_AreThereFromTheMomentTheRunIs()
    {
        var submission = await hub.AcceptedAsync();

        // A submission that has a run has figures, at nothing. One that has none has none at all —
        // there is nothing true to say about a run that does not exist (ACCESS-005).
        var figures = submission.Status.Run!;
        Assert.Equal(FastHub.Model, figures.Model);
        Assert.Equal(0, figures.TokensUsed);
        Assert.Equal(0, figures.ToolCalls);
        Assert.Equal(0, figures.EntriesLost);
    }

    [Fact]
    public async Task Figures_RiseWithTheRunAndNeverGoBackwards()
    {
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Harness.Spend(submission.Id, 51_094);
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");

        Assert.Equal(51_094, submission.Status.Run!.TokensUsed);
        Assert.Equal(1, submission.Status.Run!.ToolCalls);

        hub.Harness.Spend(submission.Id, 108_989);
        hub.Harness.Called(submission.Id, "write_page", """{"path":"ada.md"}""");

        Assert.Equal(108_989, submission.Status.Run!.TokensUsed);
        Assert.Equal(2, submission.Status.Run!.ToolCalls);

        // A streamed figure lower than one already seen is the next response counting from nothing,
        // not the run spending less (research.md R-04).
        hub.Harness.Spend(submission.Id, 12);

        Assert.Equal(108_989, submission.Status.Run!.TokensUsed);
    }

    [Fact]
    public async Task Figures_StandAsTheFinalOnes_OnceTheRunHasEnded()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Spend(submission.Id, 148_233);
        hub.Harness.Called(submission.Id, "append_log", """{"entry":"one page"}""");

        await hub.Wiki.AppendLogAsync($"Run {run.Id} wrote a page.\n", TestContext.Current.CancellationToken);
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(SubmissionState.Done, submission.State);
        Assert.Equal(148_233, submission.Status.Run!.TokensUsed);
        Assert.Equal(1, submission.Status.Run!.ToolCalls);
    }

    [Fact]
    public async Task Figures_AreReadAsOneInstantWithTheStateAndTheAcknowledgement()
    {
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Spend(submission.Id, 2_004_118);

        // The cost ceiling was reached, so the run is over and its failure is waiting to be
        // acknowledged. All of it comes out of one reading under the board's one lock: asked
        // separately, a run ending between two answers would put `running` beside a final figure —
        // a pair that never existed (contracts/hub-http-api.md).
        var status = submission.Status;

        Assert.Equal(SubmissionState.Failed, status.State);
        Assert.True(status.AwaitingAcknowledgement);
        Assert.Equal(2_004_118, status.Run!.TokensUsed);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task EntriesLost_TravelWithTheFigures()
    {
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        Assert.Equal(0, submission.Status.Run!.EntriesLost);

        hub.Record.FailWrites = true;
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");

        // The run went on and the gap is visible where the user looks (RUNS-007).
        Assert.Equal(1, submission.Status.Run!.EntriesLost);
        Assert.Equal(SubmissionState.Running, submission.State);
    }

    [Fact]
    public async Task Store_IsWrittenOnlyWhereAFigureHasRisen()
    {
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        hub.Harness.Spend(submission.Id, 51_094);
        var afterTheFirstRise = hub.Journal.Entries.Count(e => e.StartsWith("figures of", StringComparison.Ordinal));

        // The cost is reported on every streamed line, most of which change nothing. A store written
        // sixty times a turn to record the same three numbers would be sixty writes with no reader
        // (research.md R-06).
        hub.Harness.Spend(submission.Id, 51_094);
        hub.Harness.Spend(submission.Id, 12);

        Assert.Equal(
            afterTheFirstRise,
            hub.Journal.Entries.Count(e => e.StartsWith("figures of", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public async Task Figures_ComeBackWithTheRun_AfterAStop()
    {
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Spend(submission.Id, 148_233);
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");

        var restarted = hub.Restarted();
        var restored = restarted.Board.Find(submission.Id)!;

        // A run cut off by a stop reads failed and still carries what it spent. `002-ingest-queue`
        // assumed those numbers need not survive; OUT-02 gives them a reader, so they do (RUNS-010).
        Assert.Equal(SubmissionState.Failed, restored.State);
        Assert.Equal(FastHub.Model, restored.Status.Run!.Model);
        Assert.Equal(148_233, restored.Status.Run!.TokensUsed);
        Assert.Equal(1, restored.Status.Run!.ToolCalls);
    }
}

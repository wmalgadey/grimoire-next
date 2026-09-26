using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a record is: one per run, head then what happened then the tail, nothing rewritten, and a
/// write that fails counted rather than thrown (RUNS-007).
/// </summary>
/// <remarks>
/// Against the in-memory adapter at the port. That the file is where it is promised, is text, and
/// survives a stop part-way through is the real filesystem's, and the Contract suite's
/// (<c>MarkdownRunRecordTests</c>); an in-memory adapter cannot make any of those true. What it can
/// make true is a write that fails on demand, which a real disk cannot.
/// </remarks>
[Trait("level", "fast")]
public sealed class RunRecordTests
{
    private readonly FastHub hub = new();

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_IsOnePerRun()
    {
        var first = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var firstRun = hub.Conductor.Of(first.Id)!;

        hub.Harness.End(first.Id, RunOutcome.Failed);
        await hub.AcknowledgeAsync(first.Id);

        var second = await hub.AcceptedAsync("Grace Hopper found the first bug.");
        var secondRun = hub.Conductor.Of(second.Id)!;

        Assert.NotEqual(firstRun.Id, secondRun.Id);
        Assert.True(hub.Record.Holds(firstRun.Id));
        Assert.True(hub.Record.Holds(secondRun.Id));

        // Neither record holds a thing about the other run: a record is that run's and no other's.
        Assert.All(hub.Record.MomentsOf(secondRun.Id), m => Assert.Equal(secondRun.Id, m.RunId));
        Assert.Equal(firstRun.Id, hub.Record.HeadOf(firstRun.Id)!.RunId);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_HoldsTheHeadThenTheMomentsThenTheTail()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");
        hub.Harness.Did(submission.Id, Returned("# Ada\n"));

        await hub.Wiki.AppendLogAsync($"Run {run.Id} wrote a page.\n", TestContext.Current.CancellationToken);
        await hub.Harness.StoppedAsync(submission.Id);

        var written = hub.Record.Of(run.Id).ToArray();

        // The head first, the tail last, and everything the run did between them, in that order — the
        // record is read from the top and only ever grows (contracts/run-record.md).
        Assert.IsType<RunFrameHead>(written[0]);
        Assert.IsType<RunFrameTail>(written[^1]);
        Assert.Equal(2, written[1..^1].Length);
        Assert.All(written[1..^1], e => Assert.IsType<RunMoment>(e));
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_KeepsWhatWasAppended_WhenMoreArrives()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");

        var afterTheFirstCall = hub.Record.Of(run.Id);

        hub.Harness.Called(submission.Id, "write_page", """{"path":"ada.md"}""");

        // Nothing already written is changed and nothing is moved: what is there is the same
        // entries, in the same order, with one more behind them (RUNS-007).
        var afterTheSecond = hub.Record.Of(run.Id).ToArray();
        Assert.Equal(afterTheFirstCall, afterTheSecond[..afterTheFirstCall.Count]);
        Assert.Equal(afterTheFirstCall.Count + 1, afterTheSecond.Length);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_TakesNoSecondTail_WhenTheRunIsReportedEndedAgain()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Harness.End(submission.Id, RunOutcome.Failed);
        var once = hub.Record.Of(run.Id);

        // A harness whose process died after the hub had already ended the run reports again. A run
        // ends once, so nothing is written after the tail.
        hub.Record.Ended(new RunFrameTail(
            run.Id,
            FastSuite.Start,
            RunOutcome.Done,
            RunEndedBecause.StoppedWithItsLogEntry,
            TimeSpan.Zero,
            TokensUsed: 0,
            run.Ceilings,
            new Dictionary<string, ModelTokens>(StringComparer.Ordinal)));

        Assert.Equal(once, hub.Record.Of(run.Id));
        Assert.Single(hub.Record.Of(run.Id).OfType<RunFrameTail>());
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Write_IsCountedAndDoesNotThrow_WhenTheRecordCannotBeWritten()
    {
        var submission = await hub.AcceptedAsync();
        var run = hub.Conductor.Of(submission.Id)!;

        hub.Record.FailWrites = true;

        // The run goes on. Nothing here throws, which is what keeps a full disk from ending a run
        // (RUNS-007, research.md R-10).
        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");
        hub.Harness.Did(submission.Id, Returned("# Ada\n"));

        Assert.Equal(SubmissionState.Running, submission.State);
        Assert.Equal(2, hub.Record.EntriesLost(run.Id));

        hub.Record.FailWrites = false;
        hub.Harness.Called(submission.Id, "write_page", """{"path":"ada.md"}""");

        // Once a write succeeds again, the record says how many entries were lost before it — and
        // says it where they went missing, in front of the entry that got through.
        var written = hub.Record.Of(run.Id);
        var notice = Assert.Single(written.OfType<LostEntriesNotice>());
        Assert.Equal(2, notice.Count);
        Assert.Equal(written.Count - 2, written.ToList().IndexOf(notice));
    }

    private static TranscriptMoment Returned(string content) =>
        new(RunMomentKind.ToolReturned, "read_page", content);
}

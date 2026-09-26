using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// Grimoire stopped and started again: every submission is still there with the state it carried,
/// and a run that was in progress reads failed (RUNS-004).
/// </summary>
/// <remarks>
/// The rule that turns what the store held into states is the board's, and that is what is proven
/// here. That the file itself keeps them is the Contract suite's, against a real SQLite file; the
/// two together are what a restart is (research.md R-09).
/// </remarks>
[Trait("level", "fast")]
public sealed class RestartTests
{
    private readonly FastHub before = new();

    private async Task<Submission> SubmittedAsync(string text)
    {
        var accepted = await before.AcceptedAsync(text);
        before.Clock.Advance(TimeSpan.FromMinutes(1));
        return accepted;
    }

    private static Submission In(FastHub hub, Guid id) => hub.Board.Find(id)!;

    [Fact]
    [Trait("req", "RUNS-004")]
    public async Task Restart_ListsEverySubmission_WithTheStateItCarried()
    {
        var done = await SubmittedAsync("The text whose run finished.");
        before.Harness.End(done.Id, RunOutcome.Done);

        var failed = await SubmittedAsync("The text whose run failed.");
        before.Harness.End(failed.Id, RunOutcome.Failed);
        await before.AcknowledgeAsync(failed.Id);

        var waiting = await SubmittedAsync("The text that never got its turn.");
        before.Harness.ReportIn(waiting.Id);

        var after = before.Restarted();

        Assert.Equal(SubmissionState.Done, In(after, done.Id).State);
        Assert.Equal(SubmissionState.Failed, In(after, failed.Id).State);
        Assert.Equal("The text whose run finished.", In(after, done.Id).Text);
        Assert.Equal(done.SubmittedAt, In(after, done.Id).SubmittedAt);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public async Task Restart_LeavesTheRunThatWasInProgressFailed()
    {
        var running = await SubmittedAsync("The text being worked when Grimoire stopped.");
        before.Harness.ReportIn(running.Id);

        var after = before.Restarted();

        // Nothing is resumed and nothing is retried; whatever it wrote stays in the wiki.
        Assert.Equal(SubmissionState.Failed, In(after, running.Id).State);
        Assert.Empty(after.Harness.Dispatched);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public async Task Restart_LeavesTheRunThatWasInProgressFailed_BeforeTheAgentReportedIn()
    {
        // Dispatched, and the agent had not reported in yet: it reads submitted and carries a run,
        // which is what tells it apart from one still waiting its turn (research.md R-04).
        var dispatched = await SubmittedAsync("The text whose agent never reported in.");

        var after = before.Restarted();

        Assert.Equal(SubmissionState.Failed, In(after, dispatched.Id).State);
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    [Trait("req", "RUNS-004")]
    public async Task Restart_EndsTheRecordOfTheRunThatWasInProgress()
    {
        // A stop Grimoire is given the chance to act on writes the tail as the run ends. A kill or a
        // power cut does not, and then this restart is the next moment Grimoire has: without a tail
        // here the row would read failed beside a record that never says the run ended, let alone
        // why — the one reason of RUNS-008's seven that only a restart can write.
        var running = await SubmittedAsync("The text being worked when Grimoire was killed.");
        before.Harness.ReportIn(running.Id);
        before.Harness.Spend(running.Id, new Dictionary<string, ModelTokens>(StringComparer.Ordinal)
        {
            [FastHub.Model] = new(InputTokens: 12_000, OutputTokens: 400, 0, 0),
        });

        var runId = In(before, running.Id).RunId!.Value;
        var after = before.Restarted();

        var tail = after.Record.TailOf(runId);

        Assert.NotNull(tail);
        Assert.Equal(RunOutcome.Failed, tail.Outcome);
        Assert.Equal(RunEndedBecause.GrimoireStopped, tail.EndedBecause);

        // What the store kept is what the tail can say: the total the run spent survived with the
        // run, the per-model breakdown did not (RUNS-010, DEC-015). The time nobody measured is not
        // guessed from the run's start — that span is mostly the stop itself (RUNS-008).
        Assert.Equal(12_400, tail.TokensUsed);
        Assert.Empty(tail.TokensPerModel);
        Assert.Null(tail.Elapsed);
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    public async Task Restart_EndsTheRecordOfTheRunThatWasInProgress_OnlyOnce()
    {
        // The first restart ends it; from then on the run reads failed, so the second finds nothing
        // in progress. Worth holding because the guard is the store's and not the record's: a new
        // process has no memory of a tail it did not write, so a record closed twice would be closed
        // twice on disk, in a file nothing may rewrite (RUNS-007).
        var running = await SubmittedAsync("The text being worked when Grimoire was killed twice.");
        before.Harness.ReportIn(running.Id);

        var runId = In(before, running.Id).RunId!.Value;
        var after = before.Restarted().Restarted();

        Assert.Single(after.Record.Of(runId).OfType<RunFrameTail>());
    }

    [Fact]
    [Trait("req", "RUNS-008")]
    public async Task Restart_LeavesTheRecordOfARunThatHadAlreadyEnded()
    {
        // Its tail was written where the run ended, and a run ends once. A restart that closed every
        // record it found would append a second ending to this one, contradicting the first.
        var ended = await SubmittedAsync("The text whose run finished before the stop.");
        var runId = In(before, ended.Id).RunId!.Value;
        before.Harness.End(ended.Id, RunOutcome.Done);

        var after = before.Restarted();

        Assert.Single(after.Record.Of(runId).OfType<RunFrameTail>());
        Assert.Equal(RunOutcome.Done, after.Record.TailOf(runId)!.Outcome);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-002")]
    [Trait("req", "RUNS-003")]
    public async Task Restart_LeavesTheWaitingSubmissionsWaiting_UntilTheFailureIsAcknowledged()
    {
        var interrupted = await SubmittedAsync("The text being worked when Grimoire stopped.");
        before.Harness.ReportIn(interrupted.Id);
        var first = await SubmittedAsync("The text waiting behind it.");
        var second = await SubmittedAsync("The text waiting behind that.");

        var after = before.Restarted();

        // The interrupted run reads failed and holds the queue like any other failure (RUNS-003).
        Assert.Equal(SubmissionState.Submitted, In(after, first.Id).State);
        Assert.Empty(after.Harness.Dispatched);

        await after.AcknowledgeAsync(interrupted.Id);

        // And then they start, in the order they were made (RUNS-002).
        Assert.Equal([first.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));

        after.Harness.End(first.Id, RunOutcome.Done);
        Assert.Equal([first.Id, second.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-002")]
    public void Restart_StartsTheWaitingSubmissions_InTheOrderTheyWereMade()
    {
        // Two texts accepted and neither handed out — the state a stop leaves between a submission
        // being written down and the queue reaching it, and the one state where a restart finds
        // something waiting with nothing blocking it. Seeded through the store rather than driven
        // through a board, because a board that is still running would have dispatched the first.
        var journal = new HubJournal();
        var store = new InMemorySubmissionStore(journal);
        var first = Waiting("The first text nobody got to.", FastSuite.Start);
        var second = Waiting("The second text nobody got to.", FastSuite.Start.AddMinutes(1));
        store.Add(first);
        store.Add(second);

        var after = new FastHub(store, journal, new InMemoryRunRecord());

        // Started by the hub coming up, with nobody submitting anything — the fourth of the four
        // events that pump the queue (research.md R-03) — and started in the order they were made
        // rather than the order they were written down (RUNS-002).
        Assert.Equal([first.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));

        after.Harness.End(first.Id, RunOutcome.Done);

        Assert.Equal([first.Id, second.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));
    }

    private static StoredSubmission Waiting(string text, DateTimeOffset submittedAt) =>
        new(Guid.NewGuid(), text, submittedAt, SubmissionState.Submitted, Run: null, AcknowledgedAt: null);

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-003")]
    public async Task Restart_BlocksNothing_WhenTheFailureWasAcknowledgedBeforeTheStop()
    {
        var failed = await SubmittedAsync("The text whose run failed.");
        before.Harness.End(failed.Id, RunOutcome.Failed);
        await before.AcknowledgeAsync(failed.Id);
        var waiting = await SubmittedAsync("The text submitted after it.");
        before.Harness.End(waiting.Id, RunOutcome.Done);

        var after = before.Restarted();
        var later = await after.AcceptedAsync("A text submitted after the restart.");

        // The acknowledgement survived with everything else: a restart does not re-block a queue
        // the user has already cleared, and the run it cleared still reads failed.
        Assert.Equal([later.Id], after.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(SubmissionState.Failed, In(after, failed.Id).State);
        Assert.False(In(after, failed.Id).AwaitingAcknowledgement);
    }
}

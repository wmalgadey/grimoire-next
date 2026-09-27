using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The queue rule across <b>both</b> kinds: waiting work starts in the order it was made whether it
/// is a submission or a question, at most one run is in progress, and an unacknowledged failure of
/// either holds the queue (RUNS-002, RUNS-003).
/// </summary>
/// <remarks>
/// <para>
/// In process, over the board itself with in-memory adapters at the two owned ports. The queue rule is
/// the board's own judgment, made under its one lock, so there is no lower level to prove it at — and
/// nothing here waits for real time (Constitution III.6, III.7).
/// </para>
/// <para>
/// <c>QueueTests</c> beside this file proves the same rule over submissions alone and is untouched:
/// what is new is that the order is kept <em>across</em> the kinds, which is what one ordered list of
/// <c>Queued</c> is for (research.md R-03).
/// </para>
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "RUNS-002")]
public sealed class RunBoardTests
{
    private readonly FastHub hub = new();

    [Fact]
    public async Task Queue_StartsAQuestionAfterASubmissionMadeBeforeIt()
    {
        // The first submission's run holds the queue, so the question waits behind it — and behind the
        // second submission too, because that was made first.
        var first = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var second = await hub.AcceptedAsync("Grace Hopper found the first bug.");
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        Assert.NotNull(first.RunId);
        Assert.Null(second.RunId);
        Assert.Null(question.RunId);

        // The first ends, and the second submission goes — not the question, which was made after it.
        await EndAsync(first);
        Assert.NotNull(second.RunId);
        Assert.Null(question.RunId);

        // And then the question, which was next in the one list.
        await EndAsync(second);
        Assert.NotNull(question.RunId);
    }

    [Fact]
    public async Task Queue_StartsASubmissionAfterAQuestionAskedBeforeIt()
    {
        // The other way about, which is the half a board holding two lists would get wrong.
        var running = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");
        var submission = await hub.AcceptedAsync("Grace Hopper found the first bug.");

        await EndAsync(running);

        Assert.NotNull(question.RunId);
        Assert.Null(submission.RunId);

        await EndAsync(question);
        Assert.NotNull(submission.RunId);
    }

    // **There is no test here for a clock that goes backwards**, which is the case the queue's use of
    // list position rather than `SubmittedAt`/`AskedAt` exists for. `FakeTimeProvider` refuses to be
    // put back at all — "Cannot go back in time" — so the case cannot be reached with the one clock
    // double this suite has (DEC-018), and a second time double built to reach it would be a mechanism
    // with one consumer (Constitution II.1). What the two tests above do prove is the property that
    // matters: the order is the order things were accepted, across both kinds. That the order is not
    // read off a clock is visible in `TakeNext` itself, where the comment carries the reason.

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Queue_HoldsAQuestion_WhileASubmissionsFailureIsUnacknowledged()
    {
        var failing = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        await EndAsync(failing, RunOutcome.Failed);

        // A failure nobody has seen holds the queue, and it holds a question exactly as it holds a
        // submission: the reason the block exists is that the user should see what happened before
        // more work is done (RUNS-003, research.md R-12).
        Assert.Null(question.RunId);

        await hub.AcknowledgeAsync(failing.Id);
        Assert.NotNull(question.RunId);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Queue_HoldsASubmission_WhileAQuestionsFailureIsUnacknowledged()
    {
        var failing = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");
        var submission = await hub.AcceptedAsync("Grace Hopper found the first bug.");

        await EndAsync(failing, RunOutcome.Failed);

        Assert.Null(submission.RunId);

        await hub.AcknowledgeQuestionAsync(failing.Id);
        Assert.NotNull(submission.RunId);
    }

    [Fact]
    public async Task Queue_HasOneRunInProgress_AcrossBothKinds()
    {
        var submission = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // One under way, whatever the second one is. There is no separate allowance for a question
        // because it writes nothing: a run is a run, with its own ceilings and its own agent.
        Assert.Single(hub.Harness.Dispatched);
        Assert.Equal(submission.RunId, hub.Harness.Dispatched[0].RunId);
        Assert.Null(question.RunId);
    }

    [Fact]
    [Trait("req", "ACCESS-004")]
    public async Task List_AnswersWithTheSubmissionsAlone()
    {
        var submission = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // A question is not a submission: it puts nothing into the wiki and appears in no list of them,
        // so the list is exactly what it was before questions existed (research.md R-12). The chat is
        // where a question is read.
        Assert.Equal([submission], hub.Board.All);
        Assert.Single(hub.Board.Snapshot());
    }

    [Fact]
    [Trait("req", "QUERY-005")]
    public async Task List_AnswersWithTheSubmissionsAlone_WhileAQuestionIsBeingAnswered()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // Under way, and still in none of it: the list does not gain a row for a question when that
        // question's run starts either.
        Assert.NotNull(question.RunId);
        Assert.Empty(hub.Board.All);
        Assert.Empty(hub.Board.Snapshot());
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    [Trait("req", "RUNS-006")]
    [Trait("req", "QUERY-005")]
    public async Task Queue_MovesAfterARestart_WhenTheFailureThatHeldItWasAQuestions()
    {
        hub.Harness.AgentProcess = new AgentProcessIdentity(4_711, FastSuite.Start);

        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        Assert.NotNull(question.RunId);

        // Grimoire stops with the question's run in progress, and starts again. RUNS-006: the agent is
        // terminated by the identity recorded with its run — which is the whole reason that run has a
        // row while the question has none (research.md R-04).
        var restarted = hub.Restarted();

        Assert.Equal(
            new AgentProcessIdentity(4_711, FastSuite.Start),
            Assert.Single(restarted.Harness.Terminated));

        // **The block went with the chat.** A question's failure holds the queue while Grimoire runs,
        // exactly as a submission's does — but a chat does not survive a stop (QUERY-005), so there is
        // no question left on any screen to acknowledge. A block restored without its question is a
        // queue nothing can ever clear, which is worse than the gap; RUNS-003's last clause says so.
        var afterwards = await restarted.AcceptedAsync("Grace Hopper found the first bug.");

        Assert.NotNull(afterwards.RunId);

        // And the chat is empty, which is what left nothing to acknowledge.
        Assert.Empty(restarted.Chat.Turns);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-006")]
    public async Task Queue_LeavesNothingUnderWay_WhenTheRunCouldNotBeWrittenDown()
    {
        var store = new InMemorySubmissionStore();
        var failing = new FastHub(store, new HubJournal(), new InMemoryRunRecord());

        // The state file is unwritable — a locked database, a full disk. RUNS-004's whole design admits
        // this happens, which is why every write goes to disk before the queue moves.
        store.WhileWriting = () => throw new IOException("the state file could not be written");

        await Assert.ThrowsAsync<IOException>(async () => await failing.SubmitAsync("Ada Lovelace."));

        // Nothing was handed out: the board is as it was, with nothing on it at all — the write that
        // failed was the one that accepts the submission.
        Assert.Empty(failing.Board.All);

        // And nothing is being watched. Made and watched in one step, a failed write left the conductor
        // holding a run under an id the board read as still waiting — and the next pump overwrote it,
        // leaving the first run's armed ceiling to end the second (GUARD-004, RUNS-006).
        store.WhileWriting = null;

        var accepted = await failing.AcceptedAsync("Grace Hopper.");

        Assert.NotNull(accepted.RunId);
        Assert.Equal(accepted.RunId, failing.Conductor.Of(accepted.Id)!.Id);
        Assert.Single(failing.Harness.Dispatched);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Queue_WatchesNoRun_WhenAQuestionsRunCouldNotBeWrittenDown()
    {
        var store = new InMemorySubmissionStore();
        var failing = new FastHub(store, new HubJournal(), new InMemoryRunRecord());

        // A question is accepted without touching the store — nothing of it is on disk (QUERY-005) — so
        // the write that can fail is the one that records its *run*, which is what RUNS-006 needs on
        // disk to be able to kill its agent after a stop.
        var question = failing.Board.Ask("What does the wiki say?", StartUpInputs.BothPresent).Accepted!;
        failing.Chat.Ask(question);

        store.WhileWriting = () => throw new IOException("the state file could not be written");

        await Assert.ThrowsAsync<IOException>(async () => await failing.Queue.PumpAsync());

        // The question is still waiting, and no run is watched for it: what the board reads and what
        // the conductor holds agree, which is what a rollback would have had to restore afterwards.
        Assert.Null(question.RunId);
        Assert.Null(failing.Conductor.Of(question.Id));
        Assert.Empty(failing.Harness.Dispatched);
    }

    /// <summary>
    /// The run of this submission or question, ended the way the conductor ends one — through the
    /// harness, so that the queue moves by the event that moves it in the real hub (research.md R-03).
    /// </summary>
    private async Task EndAsync(Queued queued, RunOutcome outcome = RunOutcome.Done)
    {
        hub.Harness.End(
            queued.Id,
            outcome,
            outcome == RunOutcome.Done
                ? RunEndedBecause.StoppedWithItsLogEntry
                : RunEndedBecause.AgentProcessDied);

        // The queue is pumped by the ending itself, from the conductor. Awaiting nothing here would
        // read the board before the run behind it had been handed out.
        await Task.Yield();
    }
}

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

        await hub.AcknowledgeAsync(failing.Id);
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

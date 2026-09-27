using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The chat holds what was asked, the answer as it forms and the steps under it, for as long as
/// Grimoire runs and <b>whether or not a browser is connected to it</b> — and nothing of it reaches
/// any store (QUERY-005).
/// </summary>
/// <remarks>
/// The chat is memory and nothing about holding it needs a browser: no stream is subscribed to
/// anywhere in this file, which is the point — a reader who walked away comes back to what the run
/// produced (research.md R-13). The two-tabs half of QUERY-005 is its E2E end.
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "QUERY-005")]
public sealed class ChatTests
{
    private readonly FastHub hub = new();

    [Fact]
    public async Task Chat_HoldsTheAnswerAsItArrived_WhileNoBrowserIsSubscribed()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // The agent's own text, in three pieces as a stream delivers it.
        Said(question, "She wrote the first program, ");
        Said(question, "for Babbage's Analytical Engine");
        Said(question, " ([people/ada-lovelace.md](people/ada-lovelace.md)).");

        var turn = Assert.Single(hub.Chat.Turns);

        // One piece of prose, in the order it arrived — not three pieces to be joined by whoever draws
        // it, and nothing between them that the agent did not write (research.md R-08).
        Assert.Equal(question, turn.Question);
        Assert.Equal(
            "She wrote the first program, for Babbage's Analytical Engine "
                + "([people/ada-lovelace.md](people/ada-lovelace.md)).",
            turn.Answer);
    }

    [Fact]
    public async Task Chat_HoldsTheStepsUnderTheAnswer_InTheOrderTheyHappened()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        Said(question, "I will read what the wiki holds.");
        hub.Harness.Called(question.Id, "list_pages", "{}");
        Returned(question, "list_pages", """{"paths":["people/ada-lovelace.md"]}""");
        hub.Harness.Called(question.Id, "read_page", """{"path":"people/ada-lovelace.md"}""");

        var turn = Assert.Single(hub.Chat.Turns);

        // One entry per call and one per result, and the agent's own text is not among them: every
        // piece of the agent's prose is the answer, and the tool calls are the steps (research.md R-08).
        Assert.Equal(
            [
                (ChatStep.Called, "list_pages"),
                (ChatStep.Returned, "list_pages"),
                (ChatStep.Called, "read_page"),
            ],
            turn.Steps.Select(step => (step.Kind, step.Tool)));

        Assert.Equal("I will read what the wiki holds.", turn.Answer);
    }

    [Fact]
    public async Task Chat_HoldsWhatCameBackWhole()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // A page of the size a wiki page is, holding a fence and a heading of its own — which is what
        // `read_page` returns as a matter of course.
        var page = "## Ada Lovelace\n\n```\nthe first program\n```\n\n" + new string('x', 5_000);

        Returned(question, "read_page", page);

        // Never cut and never summarised. The user checking an answer is checking exactly this.
        Assert.Equal(page, Assert.Single(Assert.Single(hub.Chat.Turns).Steps).Content);
    }

    [Fact]
    public async Task Chat_HoldsEachQuestionInTheOrderItWasAsked()
    {
        var first = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");
        var second = await hub.AskedAsync("And who was her mother?");

        // Oldest first, which is the order a conversation is read in — the opposite of the submissions
        // list, which is newest first because it is a list and not a conversation.
        Assert.Equal([first, second], hub.Chat.Turns.Select(turn => turn.Question));
    }

    [Fact]
    public async Task Chat_CarriesNoCostForAQuestionWaitingItsTurn()
    {
        await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var waiting = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // No run, so there is nothing true to say about one — and **not a zero**, which would claim a
        // run that spent nothing rather than no run at all (ACCESS-008, RUNS-002).
        Assert.Null(waiting.RunId);
        Assert.Null(Assert.Single(hub.Chat.Turns).CostSpent);
        Assert.Equal(0, hub.Chat.Total);
    }

    [Fact]
    [Trait("req", "RUNS-010")]
    public async Task Chat_CarriesTheCostItsRunSpent()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        hub.Harness.Spend(question.Id, 12_000);

        // The run's own figure, and not a second count of its own: nothing is counted twice, which is
        // the seam DEC-030 named (RUNS-010).
        Assert.Equal(12_000, Assert.Single(hub.Chat.Turns).CostSpent);
        Assert.Equal(12_000, hub.Chat.Total);
    }

    [Fact]
    public async Task Chat_ReachesNoStore()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        Said(question, "She wrote the first program.");
        hub.Harness.Called(question.Id, "read_page", """{"path":"people/ada-lovelace.md"}""");
        Returned(question, "read_page", "## Ada Lovelace");

        // **Nothing of the question is on disk**: not its text, not its answer, not its steps. Held
        // means held while Grimoire runs and written down nowhere, which is what makes "nothing is
        // kept" reachable directly rather than by inspection (QUERY-005, research.md R-02).
        Assert.DoesNotContain(question.Text, hub.Store.Load().Select(s => s.Text));
        Assert.Empty(hub.Store.Load());

        // Not the record port either. A question's run has no record at all (RUNS-007).
        Assert.False(hub.Record.Holds(question.RunId!.Value));

        // And not the wiki: a question's run reads through its own two tools, and Grimoire reads
        // nothing there on its behalf — not `log.md` (RUNS-005, GUARD-005).
        Assert.Empty(hub.Wiki.Asked);
        Assert.Empty(hub.Wiki.Files);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public async Task Chat_ReachesNoStoreButItsRunsRowDoes()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // The one thing about a question that *is* on disk, and it is the run and not the question: a
        // start-up has to terminate the agent of every run it reads as having been in progress, and a
        // run with nothing written down would leave an orphaned `claude` holding the granted tools with
        // no ceiling on it (RUNS-006, research.md R-04).
        var run = Assert.Single(hub.Store.LoadRunsWithoutASubmission());

        Assert.Equal(question.RunId, run.Id);

        // With no id pointing back at the question, because nothing of the question is there to point
        // at (QUERY-005).
        Assert.Null(run.QueuedId);

        // And with its grant recorded, as every run's is (GUARD-003, GUARD-005).
        Assert.Equal(ToolGrant.ForQuestion, run.GrantedTools);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Chat_HoldsTheAnswer_WhenAnotherRunsEndingIsWhatStartedTheQuestion()
    {
        // The question waits behind a submission, so the run it is eventually given is handed out by
        // **that submission's ending**, on the harness's thread and not on the asking one. This is the
        // path the run-to-turn mapping used to be lost on: the question existed on the board before it
        // existed in the chat, so a pump that reached it in that window found no turn to map its run
        // to — and every word of the answer that followed went nowhere.
        var blocking = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");
        var question = (await hub.AskAsync("What does the wiki say about Ada Lovelace?")).Accepted!;

        Assert.Null(question.RunId);

        hub.Harness.End(blocking.Id, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry);
        await Task.Yield();

        Assert.NotNull(question.RunId);

        Said(question, "She wrote the first program.");
        hub.Harness.Called(question.Id, "read_page", """{"path":"people/ada-lovelace.md"}""");

        // The answer and the step reached the turn, which is only true if the run was mapped to it.
        var turn = Assert.Single(hub.Chat.Turns);

        Assert.Equal("She wrote the first program.", turn.Answer);
        Assert.Single(turn.Steps);
    }

    [Fact]
    [Trait("req", "RUNS-002")]
    [Trait("req", "QUERY-002")]
    public async Task Chat_ShowsTheQuestionsInTheOrderTheyWillRun_WhenSeveralAreAskedAtOnce()
    {
        const int AtOnce = 16;

        var token = TestContext.Current.CancellationToken;

        // A submission takes the one run slot, so every question below is accepted and then waits — and
        // none of them dispatches while the others are still being accepted.
        var blocking = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");

        await Task.WhenAll(Enumerable.Range(0, AtOnce)
            .Select(at => Task.Run(() => hub.AskAsync($"Question number {at}?"), token)));

        // What the chat shows, and what the queue will run: accepting a question and putting it in the
        // chat are one step, so the two cannot disagree. Two critical sections instead, and two
        // browsers asking at the same moment could be accepted in one order and appended in the other
        // — the conversation read backwards, and `ConversationSoFar` handing a follow-up's run a
        // conversation that never happened in that order (RUNS-002, QUERY-002).
        var shown = hub.Chat.Turns.Select(turn => turn.Question.Id).ToList();

        Assert.Equal(AtOnce, shown.Count);

        // Let them run, one at a time, and record the order the queue actually hands them out in.
        hub.Harness.End(blocking.Id, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry);
        await Task.Yield();

        var ran = new List<Guid>();

        while (ran.Count < AtOnce)
        {
            var running = hub.Harness.Dispatched[^1].SubmissionId;

            ran.Add(running);
            hub.Harness.End(running, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry);
            await Task.Yield();
        }

        Assert.Equal(shown, ran);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Chat_IsWrittenTo_WhileAnotherThreadHoldsTheBoard()
    {
        var store = new InMemorySubmissionStore();
        var locking = new FastHub(store, new HubJournal(), new InMemoryRunRecord());

        var question = await locking.AskedAsync("What does the wiki say about Ada Lovelace?");
        var run = question.RunId!.Value;

        var token = TestContext.Current.CancellationToken;

        using var boardIsHeld = new SemaphoreSlim(0, 1);
        using var letTheBoardGo = new SemaphoreSlim(0, 1);

        // A thread inside the board's lock, holding it. `WhileWriting` runs while whoever is writing
        // still holds it, which is what makes this deterministic rather than a race to be won.
        store.WhileWriting = () =>
        {
            store.WhileWriting = null;
            boardIsHeld.Release();
            letTheBoardGo.Wait(TimeSpan.FromSeconds(5), token);
        };

        var holding = Task.Run(() => locking.Harness.Spend(question.Id, 12_000), token);

        Assert.True(
            await boardIsHeld.WaitAsync(TimeSpan.FromSeconds(5), token), "the board's lock was never taken");

        // **The chat is written to while that lock is held elsewhere.** It must not need the board: the
        // board raises the chat's changes from inside its own lock, so a chat that took the board from
        // inside its own would give the two an order each contradicts — one thread holding the chat and
        // wanting the board, another holding the board and wanting the chat, and both waiting for ever
        // (Chat.answering).
        var written = Task.Run(() => locking.Chat.AgentSaid(run, "She wrote the first program."), token);

        Assert.True(
            await Task.WhenAny(written, Task.Delay(TimeSpan.FromSeconds(5), token)) == written,
            "writing to the chat waited for the board's lock, which is the deadlock this ordering exists to prevent");

        letTheBoardGo.Release();
        await holding;

        Assert.Equal("She wrote the first program.", Assert.Single(locking.Chat.Turns).Answer);
    }

    private void Said(Question question, string text) =>
        hub.Harness.Did(question.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, text));

    private void Returned(Question question, string tool, string content) =>
        hub.Harness.Did(question.Id, new TranscriptMoment(RunMomentKind.ToolReturned, tool, content));
}

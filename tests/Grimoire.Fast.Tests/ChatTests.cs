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

    private void Said(Question question, string text) =>
        hub.Harness.Did(question.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, text));

    private void Returned(Question question, string tool, string content) =>
        hub.Harness.Did(question.Id, new TranscriptMoment(RunMomentKind.ToolReturned, tool, content));
}

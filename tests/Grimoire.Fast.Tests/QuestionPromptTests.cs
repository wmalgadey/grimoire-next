using Grimoire.Agent;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a question's run is given: the question instruction, the purpose description, the run's
/// identifier, what has been asked and answered in this chat before it, and the question whole as the
/// user typed it — on the model Grimoire was started with, pinned (QUERY-002).
/// </summary>
/// <remarks>
/// The dispatch payload is assembled in process, as <c>DispatchPayloadTests</c> already proves for a
/// submission, so there is no lower level and nothing outside the process is involved (Constitution
/// III.6).
/// <para>
/// The last test here bears on QUERY-006 and deliberately does not carry that id: it is not registered
/// until phase 6, and a test may not name a requirement that is not in <c>docs/capabilities/</c> yet
/// (Constitution IV.2).
/// </para>
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "QUERY-002")]
public sealed class QuestionPromptTests
{
    private readonly FastHub hub = new();

    [Fact]
    public async Task Dispatch_CarriesTheFivePartsInOrder()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");
        var dispatch = Assert.Single(hub.Harness.Dispatched);

        var prompt = dispatch.Prompt;

        // In order, which is the order they are read in: what the agent is to do, what the wiki is
        // for, which run this is, and then the question (contracts/question-run.md §2).
        var instruction = prompt.IndexOf("THE QUESTION INSTRUCTION", StringComparison.Ordinal);
        var purpose = prompt.IndexOf("THE PURPOSE", StringComparison.Ordinal);
        var run = prompt.IndexOf(question.RunId!.Value.ToString(), StringComparison.Ordinal);
        var asked = prompt.IndexOf(question.Text, StringComparison.Ordinal);

        Assert.True(instruction >= 0 && purpose > instruction && run > purpose && asked > run, prompt);

        // The ingest instruction is not in it. A question's run is told what a question's run is for.
        Assert.DoesNotContain("THE INSTRUCTION\n", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_CarriesTheQuestionWholeAsItWasTyped()
    {
        // Leading and trailing whitespace and an inner newline: QUERY-003 refuses a text that is empty
        // after trimming, and what survives that is kept as it was given.
        const string AsTyped = "  What does the wiki say\nabout Ada Lovelace?  ";

        await hub.AskedAsync(AsTyped);

        Assert.EndsWith(AsTyped, Assert.Single(hub.Harness.Dispatched).Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_RunsOnTheModelGrimoireWasStartedWith()
    {
        await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // Pinned, and the same one every run gets: the model is a start-up input and not a
        // per-question choice (DEC-010).
        Assert.Equal(FastHub.Model, Assert.Single(hub.Harness.Dispatched).Model);
    }

    [Fact]
    public async Task Dispatch_CarriesNothingOfTheChat_WhenItIsTheFirstQuestion()
    {
        var question = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        // Nothing has been asked or answered before it, so the prompt carries no heading for a
        // conversation that has not happened.
        var prompt = Assert.Single(hub.Harness.Dispatched).Prompt;

        Assert.DoesNotContain("Asked:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Answered:", prompt, StringComparison.Ordinal);
        Assert.EndsWith(question.Text, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_CarriesWhatWasAskedAndAnsweredBeforeIt()
    {
        var first = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        hub.Harness.Did(first.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, "She wrote the "));
        hub.Harness.Did(first.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, "first program."));
        hub.Harness.Called(first.Id, "read_page", """{"path":"ada.md"}""");

        hub.Harness.End(first.Id, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry);
        await Task.Yield();

        var follow = await hub.AskedAsync("And who was her mother?");
        var prompt = hub.Harness.Dispatched[^1].Prompt;

        // The earlier question and the answer text it produced, in order, so that a follow-up is
        // answered in the light of what came before (QUERY-002).
        Assert.Contains($"Asked: {first.Text}", prompt, StringComparison.Ordinal);
        Assert.Contains("Answered: She wrote the first program.", prompt, StringComparison.Ordinal);

        // **The steps are not in it.** What the agent did to reach an earlier answer is for the user to
        // check, not context the next run needs — and a run's tool results are the largest thing in a
        // chat by far (research.md R-07).
        Assert.DoesNotContain("read_page", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ada.md", prompt, StringComparison.Ordinal);

        // The question being asked comes last, whole, and is not repeated as part of the conversation.
        Assert.EndsWith(follow.Text, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain($"Asked: {follow.Text}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_CarriesTheWholeChat_WithNothingTrimmedAndNoCap()
    {
        const int Questions = 40;
        var asked = new List<string>();

        for (var i = 0; i < Questions; i++)
        {
            var text = $"Question number {i} about a page in the wiki.";
            var question = await hub.AskedAsync(text);
            asked.Add(text);

            hub.Harness.Did(
                question.Id,
                new TranscriptMoment(RunMomentKind.AgentSaid, null, $"Answer number {i}, at some length."));

            hub.Harness.End(question.Id, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry);
            await Task.Yield();
        }

        await hub.AskedAsync("And one more.");
        var prompt = hub.Harness.Dispatched[^1].Prompt;

        // Every one of them, oldest first. Nothing is trimmed, there is no cap and there is no window:
        // a chat too large for a dispatch ends that run failed and the chat says so, which is the path
        // every failed run takes, with the remedy the feature already gives — a new chat (research.md
        // R-07). Dropping the oldest turns would answer a follow-up in the light of less than the chat
        // shows, silently.
        var at = -1;

        foreach (var text in asked)
        {
            var found = prompt.IndexOf($"Asked: {text}", at + 1, StringComparison.Ordinal);

            Assert.True(found > at, $"\"{text}\" is missing from the prompt, or is out of order");
            at = found;
        }
    }

    [Fact]
    public async Task Dispatch_CarriesNothingOfAQuestionThatGotNoAnswer()
    {
        var failed = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        hub.Harness.Did(failed.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, "Half a sen"));
        hub.Harness.End(failed.Id, RunOutcome.Failed, RunEndedBecause.TimeCeiling);
        await hub.AcknowledgeQuestionAsync(failed.Id);

        var follow = await hub.AskedAsync("And who was her mother?");

        // Half a sentence from a run that failed is not an answer, and nothing the run had produced is
        // presented as one (QUERY-006). So it is not context either.
        var prompt = hub.Harness.Dispatched[^1].Prompt;

        Assert.DoesNotContain("Half a sen", prompt, StringComparison.Ordinal);
        Assert.EndsWith(follow.Text, prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "QUERY-005")]
    public async Task Dispatch_CarriesNothingOfTheChatBefore_AfterANewChatWasStarted()
    {
        var answered = await hub.AskedAsync("What does the wiki say about Ada Lovelace?");

        hub.Harness.Did(answered.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, "She wrote the first program."));
        hub.Harness.End(answered.Id, RunOutcome.Done, RunEndedBecause.StoppedWithItsLogEntry);
        await Task.Yield();

        var failed = await hub.AskedAsync("And who was her mother?");

        hub.Harness.Did(failed.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, "Half a sen"));
        hub.Harness.End(failed.Id, RunOutcome.Failed, RunEndedBecause.TimeCeiling);

        // The way the chat's endpoint starts a new one: the chat put away under the board's lock, and
        // the queue asked afterwards (QUERY-005).
        hub.Board.StartANewChat(hub.Chat.Start);
        await hub.Queue.PumpAsync();

        var asked = await hub.AskedAsync("What is the Analytical Engine?");
        var prompt = hub.Harness.Dispatched[^1].Prompt;

        // A new chat is a conversation that begins here: neither earlier question, nor the answer one
        // of them got, nor what the failed one had produced reaches the run — whether it was answered
        // or not makes no difference once its chat is gone.
        Assert.DoesNotContain(answered.Text, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("She wrote the first program.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(failed.Text, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Half a sen", prompt, StringComparison.Ordinal);
        Assert.EndsWith(asked.Text, prompt, StringComparison.Ordinal);
    }
}

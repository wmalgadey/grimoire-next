using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the chat stream sends as a question changes: the one thing that changed, and the one state each question is in (ACCESS-007).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class ChatChangeTests : ChatStreamReading
{
    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_SendsTheOneThingThatChanged_AfterEachChange()
    {
        await using var hub = new HostedHub();

        // Subscribed before anything happens: the subscriber is registered when enumeration starts,
        // so everything below reaches this stream as an increment rather than as a snapshot.
        await using var stream = await hub.WatchAsync("/api/chat/events");

        Assert.Empty((await stream.NextAsync<ChatView>("chat")).Turns);

        var question = await hub.AskAsync(AboutAda);
        var id = question.ToString();

        // A question joined the chat: what was asked, when, and the state it is in. Not the chat
        // around it and not the total, which has not moved (ACCESS-007).
        var asked = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Asked, asked.Event);
        Assert.Equal(["id", "text", "askedAt", "state"], FieldsOf(asked.Data));

        Said(hub, question, "She wrote the first program.");

        // The piece of the answer that arrived, and which question it belongs under. **Nothing
        // else**: no state, no total and no steps — the browser appends this to the text node that
        // is already there, and a field it does not read would be one nobody consumes.
        var answer = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Answer, answer.Event);
        Assert.Equal(["id", "append"], FieldsOf(answer.Data));

        var appended = Read<AnswerSent>(answer.Data);

        Assert.Equal(id, appended.Id);
        Assert.Equal("She wrote the first program.", appended.Append);

        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");

        // And the step that happened, the one step and not the list it joined.
        var happened = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Step, happened.Event);
        Assert.Equal(["id", "step"], FieldsOf(happened.Data));

        var one = Read<StepSent>(happened.Data);

        Assert.Equal(id, one.Id);
        Assert.Equal(ChatStep.Called, one.Step.Kind);
        Assert.Equal("read_page", one.Step.Tool);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesTheOneStateEachQuestionIsIn()
    {
        await using var hub = new HostedHub();

        var first = await hub.AskAsync(AboutAda);
        var second = await hub.AskAsync(AboutHerMother);

        // Read off whether there is a run: nothing was ahead of the first, so it has one and reads
        // `answering`; the second has none while the first holds the one slot, and reads `waiting`
        // (RUNS-002).
        Assert.Equal(["answering", "waiting"], States(await SnapshotAsync(hub)));

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        // The first one's agent stops inside both ceilings and its process exits cleanly, which is
        // the whole of a question being answered (RUNS-005, RUNS-008) — and the second one's run then
        // starts, with nobody asking anything.
        await hub.Agent.StoppedAsync(first);
        hub.Agent.Exit(first, exitCode: 0);

        await UntilAsync(stream, first, "answered");
        await UntilAsync(stream, second, "answering");

        // And a run that ended any other way leaves its question with no answer.
        hub.Agent.End(second, RunOutcome.Failed, RunEndedBecause.TimeCeiling);

        await UntilAsync(stream, second, "no-answer");

        var chat = await SnapshotAsync(hub);

        // Four values inside one requirement, and a question is in exactly one of them: read from
        // whether there is a run and whether it has ended, never held beside the run and never two
        // at once (ACCESS-007, research.md R-03).
        Assert.Equal(["answered", "no-answer"], States(chat));

        // Which is also why a question that was answered carries no reason: a reason there would be
        // a reason for nothing.
        Assert.Null(chat.Turns[0].Because);
        Assert.NotNull(chat.Turns[1].Because);
    }

    /// <summary>The agent's own text, which is what the answer is made of (research.md R-08).</summary>
    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Asked_SaysTheQuestionIsWaiting_EvenWhereItsRunHasAlreadyStarted()
    {
        await using var hub = new HostedHub();
        await using var stream = await hub.WatchAsync("/api/chat/events");

        await stream.NextAsync<ChatView>("chat");

        // Nothing else holds the queue, so asking starts the question's run before this returns: by the
        // time the event below is read, the question is already being answered.
        await hub.AskAsync(AboutAda);

        var (name, data) = await stream.NextAsync();

        // The `asked` event records a question **joining the chat**, and a question joins it waiting its
        // turn (QUERY-001). Read at the moment it is serialised instead, it would say `answering` —
        // a state the question arrived in only afterwards, drawn for the instant before the `question`
        // event that actually reports the change (contracts/hub-http-api.md).
        Assert.Equal("asked", name);
        Assert.Contains("\"state\":\"waiting\"", data, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Asked_ReachesTheBrowser_WhileTheQuestionWaitsBehindSomethingElse()
    {
        await using var hub = new HostedHub();

        // A submission takes the one run slot, so the question is accepted and then **waits**
        // (RUNS-002). Nothing about it changes again until the queue reaches it.
        await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        await hub.AskAsync(AboutAda);

        // It is drawn while it waits. The board raises its own change before the question is on the
        // chat, so that one reaches no turn — and without the chat raising one of its own when the turn
        // is added, an accepted question would sit there unanswered *and* undrawn until something else
        // happened to change (Chat.Changed).
        var (name, data) = await stream.NextAsync();

        Assert.Equal("asked", name);
        Assert.Contains("\"state\":\"waiting\"", data, StringComparison.Ordinal);
        Assert.Contains(AboutAda, data, StringComparison.Ordinal);
    }

    /// <summary>
    /// Read forward until the chat reports this question in this state. A test names what it waits
    /// for rather than counting events, for the reason <see cref="NextNewsAsync"/> gives.
    /// </summary>
    private static async Task UntilAsync(EventStream stream, Guid question, string state)
    {
        var id = question.ToString();

        while (true)
        {
            var (name, data) = await stream.NextAsync();

            if (!string.Equals(name, ChatEvents.Question, StringComparison.Ordinal))
            {
                continue;
            }

            var changed = Read<QuestionSent>(data);

            if (string.Equals(changed.Id, id, StringComparison.Ordinal)
                && string.Equals(changed.State, state, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private static IEnumerable<string> States(ChatView chat) => chat.Turns.Select(turn => turn.State);
}

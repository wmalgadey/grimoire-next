using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The snapshot a browser is sent when it opens the chat stream, and again when it opens it anew (ACCESS-007).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class ChatSnapshotTests : ChatStreamReading
{
    [Fact]
    [Trait("req", "ACCESS-007")]
    [Trait("req", "ACCESS-008")]
    public async Task Stream_OpensWithTheChatAsItStands()
    {
        await using var hub = new HostedHub();

        // A turn with a run under it, so that the state, the steps and the figures are part of what
        // the snapshot is read for: an empty chat would say nothing about any of them.
        var question = await hub.AskAsync(AboutAda);
        Said(hub, question, "She wrote the first program.");
        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        hub.Agent.Spend(question, 12_000);

        await using var stream = await hub.WatchAsync("/api/chat/events");

        var opening = await stream.NextAsync<ChatView>("chat");
        var turn = Assert.Single(opening.Turns);

        // Every turn whole — what was asked, the answer as it stands and the steps under it — because
        // a browser that has just connected has nothing to update in place and must be given all of
        // it (ACCESS-007).
        Assert.Equal(question.ToString(), turn.Id);
        Assert.Equal(AboutAda, turn.Text);
        Assert.Equal("answering", turn.State);
        Assert.Equal("She wrote the first program.", turn.Answer);
        var step = Assert.Single(turn.Steps);

        Assert.Equal(ChatStep.Called, step.Kind);
        Assert.Equal("read_page", step.Tool);
        Assert.Equal("""{"path":"people/ada-lovelace.md"}""", step.Content);

        // The total, and the ceiling each question's own figure is written against. The figure means
        // nothing alone — input-token equivalents have no scale a reader carries in their head — so a
        // browser that only ever reads the stream must be given it too (ACCESS-008, GUARD-004).
        Assert.Equal(12_000, opening.Total);
        Assert.Equal(Ceilings.Fixed.Cost, opening.CostCeiling);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    [Trait("req", "QUERY-005")]
    public async Task Stream_OpensWithWhatArrivedWhileNobodyWasReading()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        await using (var reading = await hub.WatchAsync("/api/chat/events"))
        {
            Assert.Empty(Assert.Single((await reading.NextAsync<ChatView>("chat")).Turns).Answer);
        }

        // The tab is closed, and the run goes on writing into the chat: the chat is the hub's and not
        // a subscriber's, so what arrives now reaches nobody and is held all the same (QUERY-005).
        Said(hub, question, "She wrote the first program.");
        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        hub.Agent.Spend(question, 12_000);

        await using var again = await hub.WatchAsync("/api/chat/events");

        // A browser that subscribes again is given a fresh snapshot, and its **first** event is that
        // snapshot — not an increment replayed from a buffer, of which there is none: this subscriber
        // starts at the end of what has changed, because the snapshot already carried all of it
        // (ACCESS-007).
        var opening = await again.NextAsync<ChatView>("chat");
        var turn = Assert.Single(opening.Turns);

        Assert.Equal("She wrote the first program.", turn.Answer);
        Assert.Equal("read_page", Assert.Single(turn.Steps).Tool);
        Assert.Equal(12_000, turn.CostSpent);
        Assert.Equal(12_000, opening.Total);

        // And the next event it is sent is the next change, not the ones it was never told about —
        // which is the whole of ACCESS-007's reconnect clause, with no `Last-Event-ID` anywhere in it.
        Said(hub, question, " Her mother was Annabella Milbanke.");

        var next = await NextNewsAsync(again);

        Assert.Equal(ChatEvents.Answer, next.Event);
        Assert.Equal(" Her mother was Annabella Milbanke.", Read<AnswerSent>(next.Data).Append);
    }
}

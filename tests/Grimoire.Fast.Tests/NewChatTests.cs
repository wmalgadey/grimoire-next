using System.Text.Json;
using Grimoire.Agent;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// A new chat: what it sends, and what it lets start (QUERY-005).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class NewChatTests : ChatStreamReading
{
    [Fact]
    [Trait("req", "QUERY-005")]
    [Trait("req", "RUNS-003")]
    public async Task NewChat_StartsWhatWasWaitingBehindAFailureItTookAway()
    {
        await using var hub = new HostedHub();

        var failed = await hub.AskAsync(AboutAda);

        hub.Agent.End(failed, RunOutcome.Failed, RunEndedBecause.AgentProcessDied);

        // A failure nobody has acknowledged holds the queue (RUNS-003), so this waits.
        var waiting = await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        Assert.Null(hub.Runs.Of(waiting));

        // **Starting a new chat takes that failure off the screen**, and with it the only control that
        // could have cleared it. A failure the user can no longer see must not hold the queue — the
        // clause RUNS-003 gained for a stop, which a new chat reaches as surely. Without this the queue
        // was blocked by a question on no screen with no way to clear it, until Grimoire was restarted
        // — and a new chat is the remedy this feature offers for a failed question, so the remedy was
        // the trap.
        (await hub.PostAsync("/api/chat")).EnsureSuccessStatusCode();

        Assert.NotNull(hub.Runs.Of(waiting));
    }

    [Fact]
    [Trait("req", "QUERY-005")]
    public async Task Stream_SendsTheEmptyChat_AfterANewOneWasStarted()
    {
        await using var hub = new HostedHub();
        await using var stream = await hub.WatchAsync("/api/chat/events");

        await stream.NextAsync<ChatView>("chat");

        var question = await hub.AskAsync(AboutAda);
        Said(hub, question, "She wrote the first program.");

        (await hub.PostAsync("/api/chat")).EnsureSuccessStatusCode();

        // The increments the asking and the answering put on the stream come first; what has to arrive
        // is a fresh snapshot, and an empty one. The generation and the turns are read together, so a
        // new chat starting between the two cannot spend this subscriber's wake producing no event and
        // leave the browser showing a conversation that is gone.
        //
        // Read forward to it rather than counting what came before: how many increments one question
        // makes is not what this test is about, and pinning it would break on any change to that.
        ChatView? afresh = null;

        for (var read = 0; read < 12 && afresh is null; read++)
        {
            var (name, data) = await stream.NextAsync();

            if (name == "chat")
            {
                afresh = JsonSerializer.Deserialize<ChatView>(data);
            }
        }

        Assert.NotNull(afresh);
        Assert.Empty(afresh.Turns);
        Assert.Equal(0, afresh.Total);
    }
}

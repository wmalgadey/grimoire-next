using System.Text;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The steps under an answer: one for each thing the agent did, and what came back whole (ACCESS-007).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class StepTests : ChatStreamReading
{
    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesOneStepForEachThingTheAgentDid()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        hub.Agent.Called(question, "read_page", """{"path":"people/ada-lovelace.md"}""");
        Returned(hub, question, "read_page", "# Ada Lovelace");
        hub.Agent.Called(question, "list_pages", "{}");

        // One event per call and one per result, in the order they happened: the user checking an
        // answer reads what the agent did as a sequence, and a pair folded into one entry — or a result
        // arriving before the call it belongs to — would not be what happened (ACCESS-007).
        Assert.Equal(
            [
                (ChatStep.Called, "read_page", """{"path":"people/ada-lovelace.md"}"""),
                (ChatStep.Returned, "read_page", "# Ada Lovelace"),
                (ChatStep.Called, "list_pages", "{}"),
            ],
            [await NextStepAsync(stream), await NextStepAsync(stream), await NextStepAsync(stream)]);
    }

    [Fact]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesWhatCameBackWhole()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        await using var stream = await hub.WatchAsync("/api/chat/events");
        await stream.NextAsync<ChatView>("chat");

        var page = APageOfSomeLength();

        Returned(hub, question, "read_page", page);

        // Exactly what came back, to the character: a wiki page of some length, with its blank lines,
        // a fenced block and a name that is not ASCII. Never cut to a first line, never a count of
        // lines, never a sentence about it — the user checking an answer is checking this very text
        // against the page it came from (ACCESS-007).
        Assert.Equal((ChatStep.Returned, "read_page", page), await NextStepAsync(stream));
    }

    /// <summary>
    /// A page long enough that any cutting shows, and made of the things a wiki page is made of: blank
    /// lines, a fenced block and a non-ASCII name.
    /// </summary>
    private static string APageOfSomeLength()
    {
        var page = new StringBuilder("---\ntitle: Ada Lovelace\n---\n\n# Ada Lovelace\n\n");

        page.Append("Countess of Lovelace, née Byron — she wrote the first program.\n\n");
        page.Append("```\nBEGIN\n  note G\nEND\n```\n");

        while (page.Length < 4000)
        {
            page.Append("\nShe worked on the Analytical Engine, and on note G in particular.\n");
        }

        return page.ToString();
    }

    /// <summary>What a tool call came back with, which is the other half of the pair a run makes.</summary>
    private static void Returned(HostedHub hub, Guid question, string tool, string content) =>
        hub.Agent.Did(question, new TranscriptMoment(RunMomentKind.ToolReturned, tool, content));

    /// <summary>The next <c>step</c> event, as the three things one step is (ACCESS-007).</summary>
    private static async Task<(string Kind, string? Tool, string? Content)> NextStepAsync(EventStream stream)
    {
        var sent = await NextNewsAsync(stream);

        Assert.Equal(ChatEvents.Step, sent.Event);

        var step = Read<StepSent>(sent.Data).Step;

        return (step.Kind, step.Tool, step.Content);
    }
}

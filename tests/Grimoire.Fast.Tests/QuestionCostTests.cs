using System.Text.Json.Serialization;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a question's run spent, as the chat stream carries it (ACCESS-008).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class QuestionCostTests : ChatStreamReading
{
    [Fact]
    [Trait("req", "ACCESS-008")]
    [Trait("req", "RUNS-010")]
    public async Task Stream_CarriesWhatARunSpent_WithoutOneForAQuestionWaitingItsTurn()
    {
        await using var hub = new HostedHub();

        var answering = await hub.AskAsync(AboutAda);
        var waiting = await hub.AskAsync(AboutHerMother);

        hub.Agent.Spend(answering, 12_000);

        await using var stream = await hub.WatchAsync("/api/chat/events");

        var (name, data) = await stream.NextAsync();

        Assert.Equal(ChatEvents.Chat, name);

        var opening = Read<ChatView>(data);

        // The run's own figure, in the quantity the cost ceiling counts — never a second count of its
        // own, and never currency (RUNS-010, DEC-015).
        Assert.Equal(12_000, opening.Turns[0].CostSpent);
        Assert.Equal(12_000, opening.Total);
        Assert.Equal(Ceilings.Fixed.Cost, opening.CostCeiling);

        // The one waiting its turn has **no `costSpent` at all**, read off the JSON rather than off a
        // deserialised null: a zero would claim a run that has spent nothing rather than no run, and
        // there is nothing true to say about a run that does not exist (ACCESS-008, RUNS-002).
        var turns = TurnsOf(data);

        Assert.Contains("costSpent", FieldsOf(turns[0]));
        Assert.DoesNotContain("costSpent", FieldsOf(turns[1]));
        Assert.Equal(waiting.ToString(), Read<TurnSent>(turns[1]).Id);

        // And the total stands with **no ceiling beside it**: the one ceiling the snapshot carries is
        // the one each question's own figure is written against, and a total dressed as `x / y` would
        // invent a second that does not exist (ACCESS-008).
        Assert.Equal(
            ["costCeiling"],
            FieldsOf(data).Where(field => field.Contains("eiling", StringComparison.Ordinal)));
    }

    /// <summary>One turn of the snapshot, as far as a test reading raw JSON needs to know it.</summary>
    private sealed record TurnSent([property: JsonPropertyName("id")] string Id);
}

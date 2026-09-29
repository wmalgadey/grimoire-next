using System.Text.Json;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// A question that got no answer: why, and whether the chat still offers its acknowledgement (QUERY-006, ACCESS-003).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class UnansweredQuestionTests : ChatStreamReading
{
    [Fact]
    [Trait("req", "QUERY-006")]
    [Trait("req", "ACCESS-007")]
    public async Task Stream_CarriesWhyAQuestionGotNoAnswer()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.CostCeiling);

        var turn = Assert.Single((await SnapshotAsync(hub)).Turns);

        // The state and the reason together, in the words the chat shows: a question that got no
        // answer without one would tell the user something went wrong and nothing about what
        // (QUERY-006).
        Assert.Equal("no-answer", turn.State);
        Assert.Equal(ChatTurnView.ReasonFor(RunEndedBecause.CostCeiling), turn.Because);
        Assert.NotEmpty(turn.Because!);
    }

    [Fact]
    [Trait("req", "QUERY-006")]
    [Trait("req", "ACCESS-003")]
    public async Task Stream_OffersTheAcknowledgement_WhileTheFailureIsUnacknowledged()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);

        // There, and always `true`: the chat offers the one control on the strength of this, and a
        // failed question blocks the queue until it is used (ACCESS-003, RUNS-003).
        Assert.Equal("true", ValueOf(Assert.Single(await SnapshotTurnsAsync(hub)), "awaitingAcknowledgement"));
    }

    [Fact]
    [Trait("req", "ACCESS-003")]
    public async Task Stream_OffersNoAcknowledgement_AfterTheFailureWasAcknowledged()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);
        await AcknowledgeAsync(hub, question);

        // The key is **absent**, read off the JSON rather than off a deserialised false: the chat
        // either offers the control or says nothing about it, and a field standing there holding
        // `false` would be one the browser had to interpret (ACCESS-003).
        var turn = Assert.Single(await SnapshotTurnsAsync(hub));

        Assert.DoesNotContain("awaitingAcknowledgement", FieldsOf(turn));

        // And the question still reads *got no answer*, as an acknowledged submission still reads
        // failed: acknowledging says the user has seen it and is not a state (QUERY-006).
        Assert.Equal("no-answer", Read<QuestionSent>(turn).State);
    }

    /// <summary>
    /// The snapshot's turns, each still as the JSON it was sent as — which is how a field that is
    /// absent is told from one sent holding nothing.
    /// </summary>
    private static async Task<IReadOnlyList<string>> SnapshotTurnsAsync(HostedHub hub)
    {
        await using var stream = await hub.WatchAsync("/api/chat/events");

        var (name, data) = await stream.NextAsync();

        Assert.Equal(ChatEvents.Chat, name);

        return TurnsOf(data);
    }

    /// <summary>
    /// What one named field was sent as, read off the JSON — or null where it is absent. A field
    /// carrying <c>true</c> is told from one carrying <c>false</c>, and both from one that is not there.
    /// </summary>
    private static string? ValueOf(string json, string field)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty(field, out var value) ? value.GetRawText() : null;
    }
}

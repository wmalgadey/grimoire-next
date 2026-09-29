using System.Text.Json;
using System.Text.Json.Serialization;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The chat, sent as it changes: it opens with the whole of it, and every event after that carries
/// <b>the one thing that changed</b> and nothing else (ACCESS-007, ACCESS-008).
/// </summary>
/// <remarks>
/// <para>
/// In process, through <c>HubApplication.Build</c> with in-memory adapters at every owned port, which
/// is the application the composition root builds too (Constitution III.9). Nothing outside the
/// process is involved: loopback, no files of the wiki's, and a clock the test moves itself.
/// </para>
/// <para>
/// That <c>TypedResults.ServerSentEvents</c> frames an event, and that a browser's
/// <c>EventSource</c> reconnects, are framework and browser behaviour and are not tested
/// (Constitution III.8, research.md R-01). What is tested is what we put on the stream: each
/// event's <b>name</b> and the fields of its one line of JSON — because "the one thing that changed"
/// is half the name and half the body, and one field more would be a mechanism with no consumer
/// (Constitution II.1).
/// </para>
/// <para>
/// No test here waits for real time. Every change a stream reports is one this test makes through
/// the harness, so the event is already on its way before it is asked for (Constitution III.7). A
/// <see cref="HostedHub"/> is a real server, so there are as few of them as the scenarios allow and
/// a snapshot is read by opening another stream on the hub that is already up.
/// </para>
/// <para>
/// What every class reading the chat stream shares. The tests are split by subject into classes of
/// their own, because xunit runs the tests of one class one after another and the classes side by
/// side: a single class of thirty-two tests on a real server was the longest serial path of the
/// whole suite.
/// </para>
/// </remarks>
public abstract class ChatStreamReading
{
    private protected const string AboutAda = "What does the wiki say about Ada Lovelace?";
    private protected const string AboutHerMother = "And who was her mother?";

    /// <summary>The user has seen that this question got no answer (ACCESS-003).</summary>
    private protected static Task<HttpResponseMessage> AcknowledgeAsync(HostedHub hub, Guid question) =>
        hub.PostAsync($"/api/chat/questions/{question}/acknowledgement");

    private protected static void Said(HostedHub hub, Guid question, string text) =>
        hub.Agent.Did(question, new TranscriptMoment(RunMomentKind.AgentSaid, null, text));

    /// <summary>
    /// The chat as it now stands, read the one way a browser reads it — by opening the stream and
    /// taking the snapshot it opens with. No asked-for endpoint answers the chat.
    /// </summary>
    private protected static async Task<ChatView> SnapshotAsync(HostedHub hub)
    {
        await using var stream = await hub.WatchAsync("/api/chat/events");

        return await stream.NextAsync<ChatView>("chat");
    }

    /// <summary>
    /// The next event that is news about a turn rather than the chat saying a question changed.
    /// </summary>
    /// <remarks>
    /// A run being handed to a question is several changes at once — it left the queue, it has a run,
    /// the run reported in — and each of them is one <c>question</c> event, because the chat says that
    /// something about a question changed and never which of its facts it was. So a test reads past
    /// those to the event it is about, and everything else fails the assertion on the name rather
    /// than being skipped.
    /// </remarks>
    private protected static async Task<(string Event, string Data)> NextNewsAsync(EventStream stream)
    {
        while (true)
        {
            var sent = await stream.NextAsync();

            if (!string.Equals(sent.Event, ChatEvents.Question, StringComparison.Ordinal))
            {
                return sent;
            }
        }
    }

    /// <summary>
    /// Which fields one event's JSON actually carries — the assertion "and nothing else" is made of.
    /// </summary>
    private protected static IReadOnlyList<string> FieldsOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateObject().Select(field => field.Name)];
    }

    /// <summary>The snapshot's turns, each still as the JSON it was sent as.</summary>
    private protected static IReadOnlyList<string> TurnsOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.GetProperty("turns").EnumerateArray().Select(turn => turn.GetRawText())];
    }

    private protected static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    /// <summary>An <c>answer</c> event, which is a question and a piece of prose to append.</summary>
    private protected sealed record AnswerSent(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("append")] string Append);

    /// <summary>A <c>step</c> event, which is a question and the one step that happened.</summary>
    private protected sealed record StepSent(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("step")] ChatStepView Step);

    /// <summary>A <c>question</c> event, as far as a test reading states needs to know it.</summary>
    private protected sealed record QuestionSent(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("state")] string State);
}

using System.Net;
using Grimoire.Agent;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the chat's acknowledgement answers, and what it clears (ACCESS-003).
/// </summary>
/// <remarks>Read through <see cref="ChatStreamReading"/>, which says how and why.</remarks>
[Trait("level", "fast")]
public sealed class QuestionAcknowledgementTests : ChatStreamReading
{
    [Fact]
    public async Task Acknowledge_AnswersWithNothing_WhenItClearedAFailure()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);

        // No body either way. What the user reads afterwards is the chat, which the stream has already
        // been sent (contracts/hub-http-api.md).
        Assert.Equal(HttpStatusCode.NoContent, (await AcknowledgeAsync(hub, question)).StatusCode);
    }

    [Fact]
    public async Task Acknowledge_AnswersWithNothing_WhenItWasAcknowledgedAlready()
    {
        await using var hub = new HostedHub();

        var question = await hub.AskAsync(AboutAda);

        hub.Agent.End(question, RunOutcome.Failed, RunEndedBecause.TimeCeiling);
        await AcknowledgeAsync(hub, question);

        // The same answer for a request that cleared nothing: a page loaded before the last run failed
        // sends this, and it did exactly what it should (QUERY-006, RUNS-003).
        Assert.Equal(HttpStatusCode.NoContent, (await AcknowledgeAsync(hub, question)).StatusCode);
    }

    [Fact]
    public async Task Acknowledge_AnswersWithNothing_WhenTheQuestionIsUnknown()
    {
        await using var hub = new HostedHub();

        // A question of the chat that is gone, which is every question after a new chat is started: it
        // is on no screen and on no disk, and answering with an error would put a failure on the user's
        // screen for a request that did nothing (QUERY-005, contracts/hub-http-api.md).
        Assert.Equal(HttpStatusCode.NoContent, (await AcknowledgeAsync(hub, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_ClearsNoSubmission_WhenItsIdIsPostedToTheChat()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        hub.Agent.ReportIn(submission);
        hub.Agent.End(submission, RunOutcome.Failed);

        var behind = await hub.SubmitAsync("Grace Hopper found the first bug.");
        Assert.Null(hub.Runs.Of(behind));

        // The chat's acknowledgement addresses a **question**. Given a submission's id — which the chat
        // never showed and the user could only have from elsewhere — it clears nothing, and the failure
        // goes on holding the queue until it is acknowledged where it is shown (RUNS-003).
        var answered = await hub.PostAsync($"/api/chat/questions/{submission}/acknowledgement");

        Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode);
        Assert.Null(hub.Runs.Of(behind));

        // And the door it belongs to does clear it.
        (await hub.PostAsync($"/api/submissions/{submission}/acknowledgement")).EnsureSuccessStatusCode();

        Assert.NotNull(hub.Runs.Of(behind));
    }
}

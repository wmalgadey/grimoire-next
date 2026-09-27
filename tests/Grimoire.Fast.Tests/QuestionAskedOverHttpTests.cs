using System.Net;
using System.Net.Http.Json;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What <c>POST /api/chat/questions</c> answers with — the status and, where it refuses, the one wire
/// name that says which of the three it was (QUERY-001, QUERY-003).
/// </summary>
/// <remarks>
/// <para>
/// Through <c>HubApplication.Build</c> over loopback, because <b>these are the endpoint's answers and
/// not the board's</b>. The tests beside this file prove which <c>QuestionRefusal</c> the board
/// returns for each case; what the browser reads is the wire name the endpoint maps that to, and the
/// two could disagree without anything failing. That is the same shape of mistake that let a hub
/// serving five tools at the question door pass a test asserting what the type declared.
/// </para>
/// <para>
/// One hub for the refusals, because none of them changes anything: a refused question is stored
/// nowhere and starts no run, so the three cases cannot interfere. Each <c>HostedHub</c> starts a real
/// Kestrel and the Fast suite has 15 s in total (Constitution III.7).
/// </para>
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "QUERY-003")]
public sealed class QuestionAskedOverHttpTests
{
    [Fact]
    [Trait("req", "QUERY-001")]
    public async Task Question_IsAcceptedWithItsTurn()
    {
        await using var hub = new HostedHub();

        var response = await hub.PostAsync(
            "/api/chat/questions", new { text = "What does the wiki say about Ada Lovelace?" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var turn = await response.Content.ReadFromJsonAsync<ChatTurnView>(TestContext.Current.CancellationToken);

        // The turn as the stream's snapshot carries one, and **nothing of an answer**: the user is
        // answered before the run has produced anything, which is what QUERY-001 means by not waiting.
        Assert.Equal("What does the wiki say about Ada Lovelace?", turn!.Text);
        Assert.Equal(string.Empty, turn.Answer);
        Assert.Empty(turn.Steps);

        // And no run identifier reaches the browser: the chat addresses the question
        // (contracts/hub-http-api.md).
        Assert.DoesNotContain("runId", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "question-empty")]
    [InlineData("   ", "question-empty")]
    public async Task Question_IsRefusedWithOneReason_WhenThereIsNothingToAsk(string text, string reason)
    {
        await using var hub = new HostedHub();

        var response = await hub.PostAsync("/api/chat/questions", new { text });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var refusal = await response.Content.ReadFromJsonAsync<RefusalView>(TestContext.Current.CancellationToken);

        // The wire name the contract gives — `question-empty`, not the submission's `text-empty`. One
        // enum for both kinds would be one value meaning two different things to the page.
        Assert.Equal(reason, refusal!.Reason);

        // And a message written for the person who asked, not for a program.
        Assert.NotEmpty(refusal.Message);
    }

    [Fact]
    public async Task Question_IsRefusedForTheQuestionInstruction_WhenItIsNotThere()
    {
        await using var hub = new HostedHub();

        // The instruction is read per question, against the path the hub was started with, because
        // QUERY-003 is about the state of that path when the question is asked.
        hub.QuestionInstructionIsGone();

        var response = await hub.PostAsync("/api/chat/questions", new { text = "What does the wiki say?" });
        var refusal = await response.Content.ReadFromJsonAsync<RefusalView>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("question-instruction-missing", refusal!.Reason);
    }

    [Fact]
    public async Task Question_IsRefusedForThePurposeDescription_WhenItIsNotThere()
    {
        await using var hub = new HostedHub();

        hub.PurposeDescriptionIsGone();

        var response = await hub.PostAsync("/api/chat/questions", new { text = "What does the wiki say?" });
        var refusal = await response.Content.ReadFromJsonAsync<RefusalView>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("purpose-description-missing", refusal!.Reason);
    }

    [Fact]
    public async Task Question_IsRefusedForTheQuestionInstruction_WhenNeitherItNorThePurposeDescriptionIsThere()
    {
        await using var hub = new HostedHub();

        hub.QuestionInstructionIsGone();
        hub.PurposeDescriptionIsGone();

        // Checked in the contract's order, so each refusal names **exactly one** thing and a start with
        // neither in place says one thing rather than two.
        var response = await hub.PostAsync("/api/chat/questions", new { text = string.Empty });
        var refusal = await response.Content.ReadFromJsonAsync<RefusalView>(TestContext.Current.CancellationToken);

        Assert.Equal("question-instruction-missing", refusal!.Reason);
    }

    [Fact]
    [Trait("req", "INGEST-003")]
    public async Task Question_IsAccepted_WithoutTheIngestInstruction()
    {
        await using var hub = new HostedHub();

        // A question is refused on the *question* instruction, never on the ingest one; a submission is
        // the other way about (INGEST-003, QUERY-003).
        hub.IngestInstructionIsGone();

        var response = await hub.PostAsync("/api/chat/questions", new { text = "What does the wiki say?" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}

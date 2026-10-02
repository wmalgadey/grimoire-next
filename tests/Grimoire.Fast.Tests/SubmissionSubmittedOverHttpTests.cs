using System.Net;
using System.Net.Http.Json;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What <c>POST /api/submissions</c> refuses with — the status and the one wire name that says which
/// of the three it was (INGEST-003, INGEST-004).
/// </summary>
/// <remarks>
/// <para>
/// Through <c>HubApplication.Build</c> over loopback, because <b>these are the endpoint's answers and
/// not the board's</b>. <c>SubmissionRefusalTests</c> proves which <c>Refusal</c> the board returns;
/// what the page reads is the wire name the endpoint maps that to, and the two could disagree without
/// anything there failing — the gap <c>QuestionAskedOverHttpTests</c> closes for questions.
/// </para>
/// <para>
/// What an accepted submission answers with is read wherever a test submits through
/// <c>HostedHub.SubmitAsync</c>, so it is not repeated here. Each <c>HostedHub</c> starts a real
/// Kestrel and the Fast suite has 15 s in total (Constitution III.7).
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class SubmissionSubmittedOverHttpTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("req", "INGEST-004")]
    public async Task Submission_IsRefusedWithOneReason_WhenThereIsNoText(string text)
    {
        await using var hub = new HostedHub();

        var refusal = await RefusalAsync(await hub.PostAsync("/api/submissions", new { text }));

        // `text-empty`, not the question's `question-empty`: the page tells the two apart by name.
        Assert.Equal("text-empty", refusal.Reason);

        // And a message written for the person who submitted, not for a program.
        Assert.NotEmpty(refusal.Message);
    }

    [Fact]
    [Trait("req", "INGEST-003")]
    public async Task Submission_IsRefusedForTheInstruction_WhenItIsNotThere()
    {
        await using var hub = new HostedHub();

        // Read per submission against the path the hub was started with, so deleting it after the
        // start is what "not at that path" is (INGEST-003).
        hub.IngestInstructionIsGone();

        var refusal = await RefusalAsync(
            await hub.PostAsync("/api/submissions", new { text = "Ada Lovelace wrote the first program." }));

        Assert.Equal("instruction-missing", refusal.Reason);
    }

    [Fact]
    [Trait("req", "INGEST-003")]
    public async Task Submission_IsRefusedForThePurposeDescription_WhenItIsNotThere()
    {
        await using var hub = new HostedHub();

        hub.PurposeDescriptionIsGone();

        var refusal = await RefusalAsync(
            await hub.PostAsync("/api/submissions", new { text = "Ada Lovelace wrote the first program." }));

        Assert.Equal("purpose-description-missing", refusal.Reason);
    }

    /// <summary>The refusal as the page reads it, once its status says it is one.</summary>
    private static async Task<RefusalView> RefusalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var refusal = await response.Content.ReadFromJsonAsync<RefusalView>(TestContext.Current.CancellationToken);

        return refusal!;
    }
}

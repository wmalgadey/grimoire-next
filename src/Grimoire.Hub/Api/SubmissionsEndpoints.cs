using System.Text.Json.Serialization;
using Grimoire.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grimoire.Hub.Api;

/// <summary>
/// What the browser is told about a submission: which of the four states it is in, and the two
/// things that tell one submission from another — the opening of its text and when it was made.
/// </summary>
/// <remarks>
/// Nothing about the run. ACCESS-002 says "and no further detail" — no identifier, no step, no
/// reasoning, no duration, no cost, no history — and OUT-02 owns everything more. The excerpt and
/// the time are facts about the <em>submission</em>, which is what ACCESS-004 asks the browser to
/// show and is the only way a user can tell which text a failed run was working on (research.md
/// R-06). The wire names are on the type rather than in the host's JSON configuration, so the
/// shape this contract promises is a property of the response itself.
/// </remarks>
public sealed record SubmissionView(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("submittedAt")] DateTimeOffset SubmittedAt,
    [property: JsonPropertyName("excerpt")] string Excerpt,
    [property: JsonPropertyName("awaitingAcknowledgement")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? AwaitingAcknowledgement)
{
    public static SubmissionView Of(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        // One reading of both, under the board's lock. Asked separately, a run ending between the
        // two answers would put `running` beside an offered acknowledgement — a pair this contract
        // says cannot occur, and a control on a row whose run is still under way.
        var status = submission.Status;

        return new SubmissionView(
            submission.Id.ToString(),
            WireNameOf(status.State),
            submission.SubmittedAt,
            submission.Excerpt,

            // Absent rather than false where there is nothing to acknowledge, so that a row either
            // offers the control or says nothing at all about it. `failed` alone cannot say: an
            // acknowledged failure still reads failed and must not offer it again (RUNS-003).
            status.AwaitingAcknowledgement ? true : null);
    }

    /// <summary>Exactly one of <c>submitted</c> · <c>running</c> · <c>done</c> · <c>failed</c> (RUNS-001).</summary>
    public static string WireNameOf(SubmissionState state) => state switch
    {
        SubmissionState.Submitted => "submitted",
        SubmissionState.Running => "running",
        SubmissionState.Done => "done",
        SubmissionState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "not one of the four states"),
    };
}

/// <summary>
/// Every submission the user made, with its current state, newest first (ACCESS-002).
/// </summary>
public sealed record SubmissionListView(
    [property: JsonPropertyName("submissions")] IReadOnlyList<SubmissionView> Submissions);

/// <summary>The text the browser posts.</summary>
public sealed record SubmissionRequest([property: JsonPropertyName("text")] string? Text);

/// <summary>A refusal: a reason a program can read, and a message for the person who submitted.</summary>
public sealed record RefusalView(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// The browser's door into the hub, exactly as <c>contracts/hub-http-api.md</c> specifies it.
/// </summary>
public static class SubmissionsEndpoints
{
    /// <summary>
    /// Reads whether the two start-up inputs are in place, against the paths the hub was started
    /// with. <see cref="InstructionLoader.Read"/> is what answers it.
    /// </summary>
    public delegate StartUpInputs StartUpInputsCheck();

    public static IEndpointRouteBuilder MapSubmissions(
        this IEndpointRouteBuilder endpoints,
        SubmissionIntake intake,
        SubmissionBoard board,
        RunQueue queue,
        StartUpInputsCheck startUpInputs)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(queue);

        endpoints.MapPost("/api/submissions", async (SubmissionRequest? request) =>
        {
            // No request token reaches the run: it outlives the request that started it.
            var result = await intake.SubmitAsync(request?.Text ?? string.Empty, startUpInputs())
                .ConfigureAwait(false);

            // 202 with the accepted submission and no location: there is no endpoint for one
            // submission, and `contracts/hub-http-api.md` promises none — the browser reads the
            // list. A location pointing at a route nobody serves would be a promise that 404s.
            return result.Accepted is { } accepted
                ? Results.Json(SubmissionView.Of(accepted), statusCode: StatusCodes.Status202Accepted)
                : Refused(result.Refused!.Value);
        });

        // The browser polls this; there is no push channel. Nothing is exposed here beyond the
        // fields of a SubmissionView — one more would be a mechanism with no consumer
        // (Constitution II.1), and everything more about a run is OUT-02's.
        endpoints.MapGet("/api/submissions", () =>
            new SubmissionListView([.. board.All.Select(SubmissionView.Of)]));

        // The acknowledgement addresses a submission, which has exactly one run (INGEST-002), so
        // naming it names its failed run — and no run identifier has to reach the browser for the
        // user to clear one (research.md R-06).
        endpoints.MapPost("/api/submissions/{id:guid}/acknowledgement", async (Guid id) =>
        {
            board.Acknowledge(id);

            // Asked either way. The board decides whether anything may start, and an
            // acknowledgement that cleared nothing simply leaves it deciding no.
            await queue.PumpAsync().ConfigureAwait(false);

            // One status for both cases, deliberately: a page loaded before the last run failed
            // can acknowledge a failure that has already been cleared, and answering that with an
            // error would put a failure on the user's screen for a request that did exactly what
            // it should — nothing (contracts/hub-http-api.md).
            return Results.NoContent();
        });

        return endpoints;
    }

    private static IResult Refused(Refusal refusal)
    {
        var (status, reason, message) = refusal switch
        {
            Refusal.InstructionMissing => (
                StatusCodes.Status422UnprocessableEntity,
                "instruction-missing",
                "Grimoire's instruction file is missing. No run can start without it."),
            Refusal.PurposeDescriptionMissing => (
                StatusCodes.Status422UnprocessableEntity,
                "purpose-description-missing",
                "The purpose description is missing. No run can start without it."),
            Refusal.TextEmpty => (
                StatusCodes.Status422UnprocessableEntity,
                "text-empty",
                "There is no text to submit."),

            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "no such refusal"),
        };

        return Results.Json(new RefusalView(reason, message), statusCode: status);
    }
}

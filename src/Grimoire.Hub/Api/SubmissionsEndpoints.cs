using System.Text.Json.Serialization;
using Grimoire.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grimoire.Hub.Api;

/// <summary>What the browser is told about a submission: its state, and nothing else about the run.</summary>
/// <remarks>
/// ACCESS-002 says "and no further detail" — no step, no reasoning, no duration, no cost, no
/// history. OUT-02 owns everything more, and a field added here would be a mechanism with no
/// consumer (Constitution II.1). The wire names are on the type rather than in the host's JSON
/// configuration, so the shape this contract promises is a property of the response itself.
/// </remarks>
public sealed record SubmissionView(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("submittedAt")] DateTimeOffset SubmittedAt)
{
    public static SubmissionView Of(Submission submission) =>
        new(submission.Id.ToString(), WireNameOf(submission.State), submission.SubmittedAt);

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
        StartUpInputsCheck startUpInputs)
    {
        ArgumentNullException.ThrowIfNull(board);

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
        // three fields of a SubmissionView — a fourth would be a mechanism with no consumer
        // (Constitution II.1), and everything more about a run is OUT-02's.
        endpoints.MapGet("/api/submissions", () =>
            new SubmissionListView([.. board.All.Select(SubmissionView.Of)]));

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

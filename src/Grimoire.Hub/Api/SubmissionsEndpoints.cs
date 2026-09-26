using System.Text.Json.Serialization;
using Grimoire.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grimoire.Hub.Api;

/// <summary>
/// What the browser is told about a submission: which of the four states it is in, the two things
/// that tell one submission from another — the opening of its text and when it was made — and, where
/// it has a run, that run's model and its two figures (ACCESS-004, ACCESS-005).
/// </summary>
/// <remarks>
/// <para>
/// <b>The scope note this type carried is withdrawn.</b> It said "nothing about the run", after
/// ACCESS-002's "and no further detail", and that clause is what OUT-02 existed to undo: ACCESS-005
/// retired ACCESS-002 and replaced it with the four states plus the model and the two figures. What
/// is still not here is a run identifier — the acknowledgement and the record endpoint both address
/// the <em>submission</em>, which has exactly one run (INGEST-002), so nothing needs one. That is a
/// design property now rather than a requirement.
/// </para>
/// <para>
/// The wire names are on the type rather than in the host's JSON configuration, so the shape the
/// contract promises is a property of the response itself.
/// </para>
/// </remarks>
/// <param name="Model">
/// The pinned model id that run runs on. Recorded with the run, so an older run keeps the model it
/// actually used even after <c>--model</c> changes (DEC-010, RUNS-008).
/// </param>
/// <param name="TokensUsed">
/// Every token the run has caused so far — <b>the same quantity the cost ceiling counts</b>, over
/// every model the run touched. Never a second definition of cost, and never currency (GUARD-004,
/// DEC-015).
/// </param>
/// <param name="EntriesLost">
/// That lines are missing from that run's record, and then a number above zero. Absent where nothing
/// was lost: the run went on, and the gap is shown rather than hidden (RUNS-007).
/// </param>
public sealed record SubmissionView(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("submittedAt")] DateTimeOffset SubmittedAt,
    [property: JsonPropertyName("excerpt")] string Excerpt,
    [property: JsonPropertyName("awaitingAcknowledgement")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? AwaitingAcknowledgement,
    [property: JsonPropertyName("model")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Model = null,
    [property: JsonPropertyName("tokensUsed")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? TokensUsed = null,
    [property: JsonPropertyName("toolCalls")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? ToolCalls = null,
    [property: JsonPropertyName("entriesLost")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EntriesLost = null)
{
    public static SubmissionView Of(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        // One reading of all of it, under the board's lock. Asked separately, a run ending between
        // two answers would put `running` beside an offered acknowledgement — a pair this contract
        // says cannot occur — or beside a final figure, a pair that never existed (ACCESS-005).
        var status = submission.Status;

        return new SubmissionView(
            submission.Id.ToString(),
            WireNameOf(status.State),
            submission.SubmittedAt,
            submission.Excerpt,

            // Absent rather than false where there is nothing to acknowledge, so that a row either
            // offers the control or says nothing at all about it. `failed` alone cannot say: an
            // acknowledged failure still reads failed and must not offer it again (RUNS-003).
            status.AwaitingAcknowledgement ? true : null,

            // All four absent where there is no run, and not zeros: a submission waiting its turn has
            // no run (RUNS-002), so there is nothing true to say about one, and zeros would claim a
            // run that spent nothing rather than no run at all.
            status.Run?.Model,
            status.Run?.TokensUsed,
            status.Run?.ToolCalls,

            // And absent where nothing was lost, so that a row says lines are missing only when they
            // are. Zero would put the words on every row (RUNS-007).
            status.Run is { EntriesLost: > 0 and var lost } ? lost : null);
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
/// Every submission the user made, with its current state, newest first (ACCESS-004, ACCESS-005).
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
        // (Constitution II.1). What a run *did* is the record, served as the file it is by
        // RunRecordEndpoint, and not a second machine-shaped view of a run (ACCESS-006).
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

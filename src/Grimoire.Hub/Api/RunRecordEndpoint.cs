using Grimoire.Runs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grimoire.Hub.Api;

/// <summary>
/// One run's record, as the file holds it (ACCESS-006, RUNS-007).
/// </summary>
/// <remarks>
/// <para>
/// <b>It serves the file and renders nothing.</b> No second, machine-shaped view of a run exists
/// anywhere in this API: the browser gets exactly what the owner's editor would get, which is what
/// keeps it a window onto the record rather than a second place the run lives
/// (contracts/hub-http-api.md, US3).
/// </para>
/// <para>
/// It addresses the <em>submission</em>, which has exactly one run (INGEST-002), exactly as the
/// acknowledgement has since <c>002-ingest-queue</c>. No run identifier reaches the browser —
/// ACCESS-002's retirement lifted the rule that required that, but nothing needs one.
/// </para>
/// </remarks>
public static class RunRecordEndpoint
{
    /// <summary>
    /// Markdown, because that is what the file is. <c>charset</c> spelled out: the record holds
    /// whatever a tool returned, and a browser left to guess the encoding of that would guess.
    /// </summary>
    private const string Markdown = "text/markdown; charset=utf-8";

    public static IEndpointRouteBuilder MapRunRecord(
        this IEndpointRouteBuilder endpoints,
        SubmissionBoard board,
        IRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(record);

        endpoints.MapGet("/api/submissions/{id:guid}/record", (Guid id) =>
        {
            // Three cases, one answer: there is no such submission, it has no run yet, or its record
            // was never written at all. None of them is a run the user can read, and telling them
            // apart would say something about a run that does not exist.
            if (board.Find(id)?.RunId is not { } run || record.Read(run) is not { } bytes)
            {
                return Results.NotFound();
            }

            // The bytes, unaltered, whatever the run's state: a run under way answers with the record
            // as far as it goes, and that is the only difference between it and one from last month.
            return Results.Bytes(bytes, Markdown);
        });

        return endpoints;
    }
}

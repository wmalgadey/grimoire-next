using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json.Serialization;
using Grimoire.Runs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grimoire.Hub.Api;

/// <summary>
/// One event of a record's stream: bytes appended to the record, or how many entries of it could
/// not be written (ACCESS-006, RUNS-007).
/// </summary>
/// <remarks>
/// One type for both, with the event name saying which it is — <c>record</c> or <c>missing</c> —
/// and the field that does not apply absent. Two types would need the stream to carry
/// <c>object</c>, and what is then serialised would depend on a runtime type rather than on the
/// shape this record declares; the contract promises the shape (contracts/hub-http-api.md).
/// </remarks>
public sealed record RunRecordEvent(
    [property: JsonPropertyName("append")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Append = null,
    [property: JsonPropertyName("entriesLost")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? EntriesLost = null);

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
        RunBoard board,
        IRunRecord record,
        LiveUpdates live)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(live);

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

        // The same record, sent as it is appended to. **The one stream that carries an increment**:
        // a record reaches the low hundreds of kilobytes, so sending it whole on every growth would
        // send the whole of it once per tool result (research.md R-05).
        endpoints.MapGet("/api/submissions/{id:guid}/record/events", (Guid id, CancellationToken token) =>
        {
            // The same three cases and the same one answer as the endpoint above, asked before
            // anything is streamed: a stream that opened on a run that does not exist would leave the
            // page waiting for an event that can never come.
            if (board.Find(id)?.RunId is not { } run || record.Read(run) is null)
            {
                return Results.NotFound();
            }

            return TypedResults.ServerSentEvents(
                live.Watch(LiveUpdates.RecordOf(run), sent => Framed(record, run, sent), sent => Framed(record, run, sent), token));
        });

        return endpoints;
    }

    /// <summary>
    /// What this subscriber has not been sent: the bytes past its own offset, and a count of lost
    /// entries where that has risen since it was last told.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same function opens the stream and answers every change, which is what makes the opening
    /// event the record so far and every later one only what was appended since: the subscriber's
    /// offset starts at zero and the first call therefore sends the whole of it.
    /// </para>
    /// <para>
    /// The bytes are read through <see cref="IRunRecord.Read"/> and not rendered here.
    /// <c>MarkdownRunRecord</c> stays the only thing that renders a record, because two renderers of
    /// one record can disagree — the seam DEC-030 named for the figures (research.md R-05).
    /// </para>
    /// <para>
    /// Slicing at the offset cannot split a character: the offset is only ever moved to the length of
    /// a whole read, and a record grows by whole entries of valid UTF-8 appended after them.
    /// </para>
    /// </remarks>
    private static IEnumerable<SseItem<RunRecordEvent>> Framed(IRunRecord record, Guid run, Sent sent)
    {
        // The count first, so that a reader is told something is missing before the entry that
        // follows the gap — the record's own rule about where a gap goes (RUNS-007).
        if (record.EntriesLost(run) is var lost && lost > sent.EntriesLost)
        {
            sent.EntriesLost = lost;
            yield return new SseItem<RunRecordEvent>(new RunRecordEvent(EntriesLost: lost), "missing");
        }

        // Null where every write of this record has failed, which the connect above already refused.
        // A record that goes on to lose every later write is still a record, so this is the run's own
        // ending being written and nothing more to say.
        if (record.Read(run) is not { } bytes || bytes.Length <= sent.Bytes)
        {
            yield break;
        }

        var append = Encoding.UTF8.GetString(bytes, sent.Bytes, bytes.Length - sent.Bytes);
        sent.Bytes = bytes.Length;

        yield return new SseItem<RunRecordEvent>(new RunRecordEvent(Append: append), "record");
    }
}

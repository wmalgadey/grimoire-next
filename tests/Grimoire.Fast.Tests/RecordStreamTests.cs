using System.Globalization;
using System.Net;
using System.Text;
using Grimoire.Hub.Api;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The record's stream: the record so far on connect, only what was appended since after that, and a
/// count where entries of it could not be written (ACCESS-006, RUNS-007).
/// </summary>
/// <remarks>
/// <para>
/// In process, through <c>HubApplication.Build</c> — the same application the composition root
/// builds, with in-memory adapters at the owned ports (Constitution III.9). That
/// <c>text/event-stream</c> is framed at all is the host's doing and is not tested (III.8); what is
/// tested is the event names and the bytes they carry, because that is what this API puts on the
/// stream.
/// </para>
/// <para>
/// Addressed by the <em>submission</em> throughout, exactly as the endpoint beside it: no run
/// identifier reaches the browser (contracts/hub-http-api.md).
/// </para>
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "ACCESS-006")]
public sealed class RecordStreamTests
{
    private const string Text = "Ada Lovelace wrote the first program.";

    [Fact]
    public async Task Record_OpensWithTheRecordSoFar()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync(Text);
        var run = hub.Runs.Of(submission)!;

        await using var stream = await hub.WatchAsync($"/api/submissions/{submission}/record/events");

        var opening = await stream.NextAsync<RunRecordEvent>("record");

        // Byte for byte what the file holds: the stream is the same window onto the record as the
        // endpoint beside it, and nothing is rendered a second time.
        Assert.Equal(Encoding.UTF8.GetString(hub.Record.Read(run.Id)!), opening.Append);
        Assert.Null(opening.EntriesLost);
    }

    [Fact]
    public async Task Record_CarriesOnlyTheBytesAppendedSinceTheLastEvent_WhenItGrows()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync(Text);
        var run = hub.Runs.Of(submission)!;

        await using var stream = await hub.WatchAsync($"/api/submissions/{submission}/record/events");

        var opening = await stream.NextAsync<RunRecordEvent>("record");

        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        var grown = await stream.NextAsync<RunRecordEvent>("record");

        // Only the new bytes — the head is not sent twice, which is what keeps a record of hundreds of
        // kilobytes from being sent whole once per tool result.
        Assert.DoesNotContain($"# Run {run.Id}", grown.Append, StringComparison.Ordinal);
        Assert.Contains("read_page", grown.Append, StringComparison.Ordinal);

        // And the events so far, joined, are the whole record: nothing was skipped between them.
        Assert.Equal(
            Encoding.UTF8.GetString(hub.Record.Read(run.Id)!),
            opening.Append + grown.Append);
    }

    [Fact]
    public async Task Record_OpensWithTheWholeRecord_WhenASecondReaderJoinsAfterItGrew()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync(Text);
        var run = hub.Runs.Of(submission)!;
        var path = $"/api/submissions/{submission}/record/events";

        await using var first = await hub.WatchAsync(path);
        _ = await first.NextAsync<RunRecordEvent>("record");

        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        var increment = await first.NextAsync<RunRecordEvent>("record");

        // The offset is each subscriber's own: one that was not there for the head is owed the head,
        // and the one that already has it is owed only what followed.
        await using var second = await hub.WatchAsync(path);
        var opening = await second.NextAsync<RunRecordEvent>("record");

        Assert.Equal(Encoding.UTF8.GetString(hub.Record.Read(run.Id)!), opening.Append);
        Assert.NotEqual(increment.Append, opening.Append);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_CountsLostEntries_WhenTheCountRises()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync(Text);

        await using var stream = await hub.WatchAsync($"/api/submissions/{submission}/record/events");
        _ = await stream.NextAsync<RunRecordEvent>("record");

        // The head got through and this moment does not: a run goes on with a record that has a gap
        // in it, and the reader is told how big the gap is.
        hub.Record.FailWrites = true;
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        var missing = await stream.NextAsync<RunRecordEvent>("missing");

        Assert.Equal(1, missing.EntriesLost);
        Assert.Null(missing.Append);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_CountsLostEntriesAheadOfTheEntryAfterTheGap_WhenAWriteSucceedsAgain()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync(Text);

        await using var stream = await hub.WatchAsync($"/api/submissions/{submission}/record/events");
        _ = await stream.NextAsync<RunRecordEvent>("record");

        hub.Record.FailWrites = true;
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        var missing = await stream.NextAsync<RunRecordEvent>("missing");

        // The write succeeds again, and the entry that follows the gap arrives after the count — the
        // record's own rule about where a gap goes, kept on the stream as well as in the file.
        hub.Record.FailWrites = false;
        hub.Agent.Called(submission, "write_page", """{"path":"ada.md"}""");

        var after = await stream.NextAsync<RunRecordEvent>("record");

        Assert.Equal(1, missing.EntriesLost);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"{missing.EntriesLost} entries"),
            after.Append,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_CountsLostEntries_WhenSomeWereLostBeforeItWasOpened()
    {
        await using var hub = new HostedHub();

        // The head got through, so there is a record to read; what followed it did not, so the count
        // has already risen by the time the page is opened.
        var submission = await hub.SubmitAsync(Text);

        hub.Record.FailWrites = true;
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");
        hub.Agent.Called(submission, "read_page", """{"path":"hopper.md"}""");

        await using var stream = await hub.WatchAsync($"/api/submissions/{submission}/record/events");

        // On connect, and before the record so far: a reader is told the record is incomplete at the
        // moment it is given it, not only when the count next rises.
        var missing = await stream.NextAsync<RunRecordEvent>("missing");

        Assert.Equal(2, missing.EntriesLost);
    }

    [Fact]
    public async Task Record_IsNotFound_WhenThereIsNoSuchSubmission()
    {
        await using var hub = new HostedHub();

        var response = await hub.GetAsync($"/api/submissions/{Guid.NewGuid()}/record/events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_IsNotFound_WhenTheSubmissionHasNoRunYet()
    {
        await using var hub = new HostedHub();

        // The first submission's run holds the queue, so the second waits its turn and has no run
        // (RUNS-002). A stream opened on it would leave the page waiting for an event that can never
        // come.
        await hub.SubmitAsync(Text);
        var waiting = await hub.SubmitAsync("Grace Hopper found the first bug.");

        var response = await hub.GetAsync($"/api/submissions/{waiting}/record/events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_IsNotFound_WhenItWasNeverWrittenAtAll()
    {
        await using var hub = new HostedHub(recordEverythingFails: true);

        var submission = await hub.SubmitAsync(Text);

        // Every write failed, the head included, so there is no record to open a stream on — and the
        // run went on all the same, which is what the count says (RUNS-007).
        var response = await hub.GetAsync($"/api/submissions/{submission}/record/events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, hub.Record.EntriesLost(hub.Runs.Of(submission)!.Id));
    }
}

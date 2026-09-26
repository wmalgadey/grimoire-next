using System.Net;
using System.Text;
using Grimoire.Agent;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The record endpoint: the file's bytes unaltered, and <c>404</c> where there is no run to read
/// (ACCESS-006).
/// </summary>
/// <remarks>
/// In process, through <c>HubApplication.Build</c> — the same application every suite builds, with
/// in-memory adapters at the owned ports (Constitution III.9). That <c>text/markdown</c> is served is
/// the host's doing and is not tested (III.8); what is tested is that the body is the record and not
/// a rendering of it.
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "ACCESS-006")]
public sealed class RunRecordEndpointTests
{
    [Fact]
    public async Task Record_IsAnsweredWithItsBytesUnaltered()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        var run = hub.Runs.Of(submission)!;

        hub.Agent.ReportIn(submission);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        var response = await hub.GetAsync($"/api/submissions/{submission}/record");
        var served = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The bytes the record holds, byte for byte. Nothing is rendered, nothing is summarised, and
        // no second machine-shaped view of a run exists (contracts/hub-http-api.md).
        Assert.Equal(hub.Record.Read(run.Id), served);
        Assert.Contains($"# Run {run.Id}", Encoding.UTF8.GetString(served), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_IsAnsweredWhileTheRunIsStillUnderWay()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        hub.Agent.ReportIn(submission);

        var response = await hub.GetAsync($"/api/submissions/{submission}/record");

        // A run under way answers with the record as far as it goes — the head, and however many
        // moments have happened. That is the only difference between it and one from last month.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var served = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Started", served, StringComparison.Ordinal);
        Assert.DoesNotContain("ended", served, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_IsNotFound_WhenThereIsNoSuchSubmission()
    {
        await using var hub = new HostedHub();

        var response = await hub.GetAsync($"/api/submissions/{Guid.NewGuid()}/record");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_IsNotFound_WhenTheSubmissionHasNoRunYet()
    {
        await using var hub = new HostedHub();

        // The first submission's run holds the queue, so the second waits its turn and has no run
        // (RUNS-002). There is nothing true to say about a run that does not exist.
        await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        var waiting = await hub.SubmitAsync("Grace Hopper found the first bug.");

        var response = await hub.GetAsync($"/api/submissions/{waiting}/record");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("req", "RUNS-007")]
    public async Task Record_IsNotFound_WhenItWasNeverWrittenAtAll()
    {
        await using var hub = new HostedHub(recordEverythingFails: true);

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        // Every write failed, the head included, so there is no record — and the run went on all the
        // same, which is what the count on the row says (RUNS-007).
        var response = await hub.GetAsync($"/api/submissions/{submission}/record");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, hub.Record.EntriesLost(hub.Runs.Of(submission)!.Id));
    }
}

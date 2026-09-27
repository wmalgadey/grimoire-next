using System.Text.Json;
using Grimoire.Agent;
using Grimoire.Hub.Api;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The submissions stream: the list, sent rather than asked for — the same body the list answers
/// with, and the whole of it every time (ACCESS-005).
/// </summary>
/// <remarks>
/// <para>
/// In process, through <c>HubApplication.Build</c> with in-memory adapters at every owned port, which
/// is the application the composition root builds too (Constitution III.9). Nothing outside the
/// process is involved: loopback, no files of the wiki's, and a clock the test moves itself.
/// </para>
/// <para>
/// That <c>TypedResults.ServerSentEvents</c> frames an event is framework behaviour and is not tested
/// (Constitution III.8, research.md R-01). What is tested is what we put on the stream: the event's
/// name, and that its one line of JSON is the list — whole, and read as one instant.
/// </para>
/// <para>
/// No test here waits for real time. Every change a stream reports is one this test makes through the
/// harness, so the event is already on its way before it is asked for (Constitution III.7).
/// </para>
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "ACCESS-005")]
public sealed class SubmissionStreamTests
{
    /// <summary>
    /// The list as the asked-for endpoint answers with it at this moment, read through the same
    /// deserialiser the stream's events are read through — so that a difference between the two is a
    /// difference in what was sent and not in how it was read.
    /// </summary>
    private static async Task<SubmissionListView> ListAsync(HostedHub hub)
    {
        var response = await hub.GetAsync("/api/submissions");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return JsonSerializer.Deserialize<SubmissionListView>(body)!;
    }

    [Fact]
    public async Task Stream_OpensWithTheListAsItStands()
    {
        await using var hub = new HostedHub();

        // A row with a run on it, so that the model and the figures are part of what is compared: a
        // comparison of two empty lists would hold whatever the two sides did with a submission.
        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        hub.Agent.ReportIn(submission);
        hub.Agent.Spend(submission, 12_000);

        await using var stream = await hub.WatchAsync("/api/submissions/events");

        var opening = await stream.NextAsync<SubmissionListView>("submissions");

        // Nothing changed between the snapshot and this reading, so the two are the same list, and
        // the stream is one more way of reading the one shape rather than a second shape beside it
        // (contracts/hub-http-api.md).
        var asked = await ListAsync(hub);

        Assert.Equal(asked.Submissions, opening.Submissions);

        // The ceiling included. The figure beside it means nothing alone — input-token equivalents
        // have no scale a reader carries in their head — so a browser that only ever reads the
        // stream must be given it too (ACCESS-005, docs/ux.md).
        Assert.Equal(asked.CostCeiling, opening.CostCeiling);
        Assert.Equal(Ceilings.Fixed.Cost, opening.CostCeiling);
    }

    [Fact]
    public async Task Stream_SendsTheWholeList_WhenOneSubmissionChanged()
    {
        await using var hub = new HostedHub();

        // The first one's run holds the queue, so the second waits its turn with no run at all
        // (RUNS-002) — which is how one list comes to hold a `running` row and a `submitted` one.
        var running = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        var waiting = await hub.SubmitAsync("Grace Hopper found the first bug.");

        await using var stream = await hub.WatchAsync("/api/submissions/events");

        var opening = await stream.NextAsync<SubmissionListView>("submissions");
        Assert.Equal(2, opening.Submissions.Count);

        // One submission changes, and only one: a tool call raises the figures of the run that is
        // under way and says nothing about the one behind it.
        hub.Agent.ReportIn(running);
        hub.Agent.Called(running, "read_page", """{"path":"ada.md"}""");

        var sent = await stream.NextAsync<SubmissionListView>("submissions");

        // Every submission, never only the one that changed. The list is read as one instant under
        // the board's one lock, and a delta would break exactly that: a browser holding rows from
        // two different readings would show a pair of states that never stood together (ACCESS-005,
        // research.md R-05).
        Assert.Equal(
            [waiting.ToString(), running.ToString()],
            sent.Submissions.Select(s => s.Id));

        // Newest first, and the one that did not change carried along unaltered — including having
        // no run, which is what makes it the whole list and not a patch.
        Assert.Equal("submitted", sent.Submissions[0].State);
        Assert.Null(sent.Submissions[0].Model);
        Assert.Equal("running", sent.Submissions[1].State);
    }

    [Fact]
    [Trait("req", "RUNS-010")]
    public async Task Stream_SendsTheFiguresTheListAnswersWith_AfterARunSpent()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        hub.Agent.ReportIn(submission);

        await using var stream = await hub.WatchAsync("/api/submissions/events");

        await stream.NextAsync<SubmissionListView>("submissions");

        hub.Agent.Spend(submission, 12_000);
        hub.Agent.Called(submission, "read_page", """{"path":"ada.md"}""");

        // A figure rose, so an event went out — the same list, carrying the figures as they now
        // stand (RUNS-010).
        SubmissionView? row = null;
        while (row is not { CostSpent: 12_000, ToolCalls: 1 })
        {
            // The cost and the tool call are two changes and may arrive as one event or two: the
            // board tells the browser something changed, never which of the figures it was.
            var sent = await stream.NextAsync<SubmissionListView>("submissions");
            row = Assert.Single(sent.Submissions);
        }

        // And they are the figures the list itself answers with at this same moment: one reading of
        // the submission's status, not two. Asked apart, a run ending between them would put a
        // running row's figure beside a final one (ACCESS-005, RUNS-010).
        var asked = Assert.Single((await ListAsync(hub)).Submissions);

        Assert.Equal(asked, row);
        Assert.Equal(FastHub.Model, row.Model);
    }

    [Fact]
    public async Task Stream_SendsTheNewState_AfterARunEnded()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        hub.Agent.ReportIn(submission);

        await using var stream = await hub.WatchAsync("/api/submissions/events");

        var opening = await stream.NextAsync<SubmissionListView>("submissions");
        Assert.Equal("running", Assert.Single(opening.Submissions).State);

        hub.Agent.End(submission, RunOutcome.Failed);

        var sent = await stream.NextAsync<SubmissionListView>("submissions");
        var row = Assert.Single(sent.Submissions);

        // The state, and the acknowledgement the failure now waits for. Both are on the row rather
        // than announced as events of their own: the page updates its rows in place from the list it
        // is given, keyed by the submission's id (ACCESS-005, RUNS-003).
        Assert.Equal("failed", row.State);
        Assert.True(row.AwaitingAcknowledgement);

        // The same list the asked-for endpoint answers with, still — a stream that drifted from it
        // once something ended would be a second shape (contracts/hub-http-api.md).
        Assert.Equal(Assert.Single((await ListAsync(hub)).Submissions), row);
    }

    [Fact]
    public async Task Stream_SendsTheListWithoutTheControl_AfterAFailureWasAcknowledged()
    {
        await using var hub = new HostedHub();

        var submission = await hub.SubmitAsync("Ada Lovelace wrote the first program.");
        hub.Agent.ReportIn(submission);
        hub.Agent.End(submission, RunOutcome.Failed);

        await using var stream = await hub.WatchAsync("/api/submissions/events");

        var opening = await stream.NextAsync<SubmissionListView>("submissions");
        Assert.True(Assert.Single(opening.Submissions).AwaitingAcknowledgement);

        var acknowledged = await hub.PostAsync($"/api/submissions/{submission}/acknowledgement");
        acknowledged.EnsureSuccessStatusCode();

        // The third of the three things that send the list: not a state and not a figure, but the
        // user saying they have seen the failure. Without it the row would go on offering a control
        // that does nothing until something else about some submission happened to change
        // (ACCESS-003, ACCESS-005, RUNS-003).
        var sent = await stream.NextAsync<SubmissionListView>("submissions");
        var row = Assert.Single(sent.Submissions);

        // An acknowledged failure still reads failed — what it no longer does is ask to be
        // acknowledged again (RUNS-003).
        Assert.Equal("failed", row.State);
        Assert.Null(row.AwaitingAcknowledgement);
    }
}

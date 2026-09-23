using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The moment a submission is made. A text is accepted whatever else is under way and the user
/// waits for no run to end (INGEST-001); what happens to it afterwards is the queue's (RUNS-002,
/// <see cref="QueueTests"/>).
/// </summary>
[Trait("level", "fast")]
public sealed class SubmissionAcceptanceTests
{
    private readonly FastHub hub = new();

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_IsAccepted_WithTheTextAsItWasGiven()
    {
        var result = await hub.SubmitAsync("Ada Lovelace wrote the first program.");

        Assert.NotNull(result.Accepted);
        Assert.Null(result.Refused);
        Assert.Equal("Ada Lovelace wrote the first program.", result.Accepted!.Text);
        Assert.Equal(FastSuite.Start, result.Accepted.SubmittedAt);
        Assert.NotEqual(Guid.Empty, result.Accepted.Id);
        Assert.Equal([result.Accepted], hub.Board.All);
    }

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_IsAccepted_WithoutWaitingForTheRunToEnd()
    {
        var result = await hub.SubmitAsync("A text.");

        // The call has returned, the run was dispatched, and nothing has ended it: the user is
        // free while the agent works. Were acceptance waiting for the run, this line would not
        // be reached with the run still under way.
        Assert.NotNull(result.Accepted);
        Assert.True(hub.Harness.RunUnderWay);
        Assert.Equal(SubmissionState.Submitted, result.Accepted!.State);
    }

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_StartsARun_WithItsOwnIdentifier()
    {
        var first = await hub.SubmitAsync("First.");
        hub.Harness.End(first.Accepted!.Id, RunOutcome.Done);
        var second = await hub.SubmitAsync("Second.");

        Assert.Equal(
            [first.Accepted!.Id, second.Accepted!.Id],
            hub.Harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(
            [FastHub.Prompt("First.", hub.Harness.Dispatched[0].RunId), FastHub.Prompt("Second.", hub.Harness.Dispatched[1].RunId)],
            hub.Harness.Dispatched.Select(d => d.Prompt));

        // A run identifier is a fresh GUID per run, never the submission's (data-model.md §Run).
        Assert.Equal(2, hub.Harness.Dispatched.Select(d => d.RunId).Distinct().Count());
        Assert.DoesNotContain(hub.Harness.Dispatched, d => d.RunId == d.SubmissionId);
    }

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_IsAccepted_AfterADispatchThatCouldNotStart()
    {
        hub.Harness.DispatchFailure = new InvalidOperationException("the agent process would not start");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hub.SubmitAsync("The first text."));

        hub.Harness.DispatchFailure = null;

        // A run that never began is not a run in progress. Were the submission left reading
        // Submitted, it would refuse every later text for as long as the process lives.
        Assert.NotNull((await hub.SubmitAsync("The second text.")).Accepted);
    }
}

using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The moment a submission is made: accepted when nothing is running, refused when something is
/// (INGEST-001, INGEST-005).
/// </summary>
[Trait("level", "fast")]
public sealed class SubmissionAcceptanceTests
{
    private readonly InMemoryAgentHarness harness = new();
    private readonly SubmissionBoard board = new(FastSuite.Clock());
    private readonly SubmissionIntake intake;

    public SubmissionAcceptanceTests() => intake = new SubmissionIntake(board, harness);

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_IsAccepted_WhenNoRunIsInProgress()
    {
        var result = await intake.SubmitAsync("Ada Lovelace wrote the first program.", StartUpInputs.BothPresent);

        Assert.NotNull(result.Accepted);
        Assert.Null(result.Refused);
        Assert.Equal("Ada Lovelace wrote the first program.", result.Accepted!.Text);
        Assert.Equal(FastSuite.Start, result.Accepted.SubmittedAt);
        Assert.NotEqual(Guid.Empty, result.Accepted.Id);
        Assert.Equal([result.Accepted], board.All);
    }

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_IsAccepted_WithoutWaitingForTheRunToEnd()
    {
        var result = await intake.SubmitAsync("A text.", StartUpInputs.BothPresent);

        // The call has returned, the run was dispatched, and nothing has ended it: the user is
        // free while the agent works. Were acceptance waiting for the run, this line would not
        // be reached with the run still under way.
        Assert.NotNull(result.Accepted);
        Assert.True(harness.RunUnderWay);
        Assert.Equal(SubmissionState.Submitted, result.Accepted!.State);
    }

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_StartsARun_WithItsOwnIdentifier()
    {
        var first = await intake.SubmitAsync("First.", StartUpInputs.BothPresent);
        harness.End(first.Accepted!.Id, RunOutcome.Done);
        var second = await intake.SubmitAsync("Second.", StartUpInputs.BothPresent);

        Assert.Equal(
            [first.Accepted!.Id, second.Accepted!.Id],
            harness.Dispatched.Select(d => d.SubmissionId));
        Assert.Equal(["First.", "Second."], harness.Dispatched.Select(d => d.Text));

        // A run identifier is a fresh GUID per run, never the submission's (data-model.md §Run).
        Assert.Equal(2, harness.Dispatched.Select(d => d.RunId).Distinct().Count());
        Assert.DoesNotContain(harness.Dispatched, d => d.RunId == d.SubmissionId);
    }

    [Fact]
    [Trait("req", "INGEST-005")]
    public async Task Submit_IsRefused_WhileRunInProgress()
    {
        var accepted = await intake.SubmitAsync("The first text.", StartUpInputs.BothPresent);
        harness.ReportIn(accepted.Accepted!.Id);

        var result = await intake.SubmitAsync("The second text.", StartUpInputs.BothPresent);

        Assert.Equal(Refusal.RunInProgress, result.Refused);
        Assert.Null(result.Accepted);
    }

    [Fact]
    [Trait("req", "INGEST-005")]
    public async Task Submit_IsNotStored_WhenRefused()
    {
        var accepted = await intake.SubmitAsync("The first text.", StartUpInputs.BothPresent);

        await intake.SubmitAsync("The second text.", StartUpInputs.BothPresent);

        // Nothing about the refused text is kept: it is not a Submission and carries no state.
        Assert.Equal([accepted.Accepted!], board.All);
    }

    [Fact]
    [Trait("req", "INGEST-005")]
    public async Task Submit_StartsNoRun_WhenRefused()
    {
        await intake.SubmitAsync("The first text.", StartUpInputs.BothPresent);

        await intake.SubmitAsync("The second text.", StartUpInputs.BothPresent);

        Assert.Single(harness.Dispatched);
    }

    [Fact]
    [Trait("req", "INGEST-001")]
    public async Task Submit_IsAccepted_AfterADispatchThatCouldNotStart()
    {
        harness.DispatchFailure = new InvalidOperationException("the agent process would not start");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => intake.SubmitAsync("The first text.", StartUpInputs.BothPresent));

        harness.DispatchFailure = null;

        // A run that never began is not a run in progress. Were the submission left reading
        // Submitted, it would refuse every later text for as long as the process lives.
        Assert.NotNull((await intake.SubmitAsync("The second text.", StartUpInputs.BothPresent)).Accepted);
    }

    [Fact]
    [Trait("req", "INGEST-005")]
    public async Task Submit_IsAccepted_AfterTheRunHasEnded()
    {
        var first = await intake.SubmitAsync("The same text.", StartUpInputs.BothPresent);
        Assert.Equal(Refusal.RunInProgress, (await intake.SubmitAsync("The same text.", StartUpInputs.BothPresent)).Refused);

        harness.End(first.Accepted!.Id, RunOutcome.Done);

        // Nothing was wrong with the request, only with the moment (contracts/hub-http-api.md).
        Assert.NotNull((await intake.SubmitAsync("The same text.", StartUpInputs.BothPresent)).Accepted);
    }
}

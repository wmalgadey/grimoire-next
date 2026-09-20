using System.Text.Json;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The four states and the transitions of data-model.md (RUNS-001), and the hub-side half of
/// ACCESS-002: the response carries the state and nothing else about the run.
/// </summary>
[Trait("level", "fast")]
public sealed class SubmissionStateTests
{
    private readonly InMemoryAgentHarness harness = new();
    private readonly SubmissionBoard board = new(FastSuite.Clock());
    private readonly SubmissionIntake intake;

    public SubmissionStateTests() => intake = new SubmissionIntake(board, harness);

    private async Task<Submission> Accepted(string text = "A text.")
    {
        var result = await intake.SubmitAsync(text, StartUpInputs.BothPresent);
        return result.Accepted!;
    }

    [Fact]
    [Trait("req", "RUNS-001")]
    public void States_AreTheFourTheSpecNames() =>
        Assert.Equal(
            [SubmissionState.Submitted, SubmissionState.Running, SubmissionState.Done, SubmissionState.Failed],
            Enum.GetValues<SubmissionState>());

    [Fact]
    [Trait("req", "RUNS-001")]
    public async Task AgentReportsIn_TurnsSubmittedIntoRunning()
    {
        var submission = await Accepted();

        Assert.Equal(SubmissionState.Submitted, submission.State);

        harness.ReportIn(submission.Id);

        Assert.Equal(SubmissionState.Running, submission.State);
    }

    [Theory]
    [InlineData(RunOutcome.Done, SubmissionState.Done)]
    [InlineData(RunOutcome.Failed, SubmissionState.Failed)]
    [Trait("req", "RUNS-001")]
    public async Task RunEnds_LeavesTheSubmissionDoneOrFailed(RunOutcome outcome, SubmissionState state)
    {
        var submission = await Accepted();
        harness.ReportIn(submission.Id);

        harness.End(submission.Id, outcome);

        Assert.Equal(state, submission.State);
    }

    [Fact]
    [Trait("req", "RUNS-001")]
    public async Task RunEnds_LeavesTheSubmissionFailed_WithoutTheAgentReportingIn()
    {
        // The window between acceptance and system/init is where the grant is checked, and a
        // surface that is not the grant ends the run failed there (data-model.md §SubmissionState).
        var submission = await Accepted();

        harness.End(submission.Id, RunOutcome.Failed);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Theory]
    [InlineData(RunOutcome.Done)]
    [InlineData(RunOutcome.Failed)]
    [Trait("req", "RUNS-001")]
    public async Task Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed(RunOutcome outcome)
    {
        var submission = await Accepted();
        harness.End(submission.Id, outcome);
        var terminal = submission.State;

        // There is no transition out of either in this feature — acknowledgement is RUNS-003,
        // held back by the split.
        Assert.Throws<InvalidOperationException>(() => submission.AgentReportedIn());
        Assert.Throws<InvalidOperationException>(() => submission.Ended(SubmissionState.Done));
        Assert.Equal(terminal, submission.State);
    }

    [Fact]
    [Trait("req", "RUNS-001")]
    public async Task Transitions_LeaveExactlyOneStateAtATime()
    {
        var submission = await Accepted();

        foreach (var reached in new[] { SubmissionState.Submitted, SubmissionState.Running, SubmissionState.Done })
        {
            Assert.Single(Enum.GetValues<SubmissionState>(), s => s == submission.State);
            Assert.Equal(reached, submission.State);

            if (reached == SubmissionState.Submitted)
            {
                harness.ReportIn(submission.Id);
            }
            else if (reached == SubmissionState.Running)
            {
                harness.End(submission.Id, RunOutcome.Done);
            }
        }
    }

    [Fact]
    [Trait("req", "ACCESS-002")]
    public async Task Report_CarriesNothingBeyondTheState()
    {
        var submission = await Accepted();
        harness.ReportIn(submission.Id);

        var json = JsonSerializer.SerializeToElement(SubmissionView.Of(submission));

        // No step, reasoning, duration, cost or history — ACCESS-002 says "and no further
        // detail", and OUT-02 owns everything more (contracts/hub-http-api.md).
        Assert.Equal(
            ["id", "state", "submittedAt"],
            json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(submission.Id.ToString(), json.GetProperty("id").GetString());
        Assert.Equal("running", json.GetProperty("state").GetString());
    }

    [Theory]
    [InlineData(SubmissionState.Submitted, "submitted")]
    [InlineData(SubmissionState.Running, "running")]
    [InlineData(SubmissionState.Done, "done")]
    [InlineData(SubmissionState.Failed, "failed")]
    [Trait("req", "ACCESS-002")]
    public void Report_NamesEachStateAsOneOfTheFour(SubmissionState state, string wire) =>
        Assert.Equal(wire, SubmissionView.WireNameOf(state));
}

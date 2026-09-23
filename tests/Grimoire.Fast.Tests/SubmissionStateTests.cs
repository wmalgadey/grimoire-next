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
    private readonly FastHub hub = new();

    private Task<Submission> Accepted(string text = "A text.") => hub.AcceptedAsync(text);

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

        hub.Harness.ReportIn(submission.Id);

        Assert.Equal(SubmissionState.Running, submission.State);
    }

    [Theory]
    [InlineData(RunOutcome.Done, SubmissionState.Done)]
    [InlineData(RunOutcome.Failed, SubmissionState.Failed)]
    [Trait("req", "RUNS-001")]
    public async Task RunEnds_LeavesTheSubmissionDoneOrFailed(RunOutcome outcome, SubmissionState state)
    {
        var submission = await Accepted();
        hub.Harness.ReportIn(submission.Id);

        hub.Harness.End(submission.Id, outcome);

        Assert.Equal(state, submission.State);
    }

    [Fact]
    [Trait("req", "RUNS-001")]
    public async Task RunEnds_LeavesTheSubmissionFailed_WithoutTheAgentReportingIn()
    {
        // The window between acceptance and system/init is where the grant is checked, and a
        // surface that is not the grant ends the run failed there (data-model.md §SubmissionState).
        var submission = await Accepted();

        hub.Harness.End(submission.Id, RunOutcome.Failed);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Theory]
    [InlineData(RunOutcome.Done)]
    [InlineData(RunOutcome.Failed)]
    [Trait("req", "RUNS-001")]
    public async Task Transition_IsRefused_WhenTheSubmissionIsAlreadyDoneOrFailed(RunOutcome outcome)
    {
        var submission = await Accepted();
        hub.Harness.End(submission.Id, outcome);
        var terminal = submission.State;

        // There is no transition out of either. Acknowledging a failure is not one: the
        // acknowledged run still reads failed (RUNS-003).
        Assert.Throws<InvalidOperationException>(() => hub.Board.ReportedIn(submission.Id));
        Assert.Throws<InvalidOperationException>(() => hub.Board.Ended(submission.Id, SubmissionState.Done));
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
                hub.Harness.ReportIn(submission.Id);
            }
            else if (reached == SubmissionState.Running)
            {
                hub.Harness.End(submission.Id, RunOutcome.Done);
            }
        }
    }

    [Fact]
    [Trait("req", "RUNS-001")]
    public async Task RunEnds_IsIgnored_WhenTheRunHasAlreadyEnded()
    {
        var submission = await Accepted();
        hub.Harness.ReportIn(submission.Id);

        // The log names the run, so the stop ends it done rather than nudging it. Without that
        // the run would still be under way and the second report below would be no second report
        // at all.
        var run = hub.Conductor.Of(submission.Id)!;
        await hub.Wiki.AppendLogAsync($"Run {run.Id} wrote a page.\n", TestContext.Current.CancellationToken);
        await hub.Harness.StoppedAsync(submission.Id);
        Assert.Equal(SubmissionState.Done, submission.State);

        // Now a harness whose process died after the hub had already ended the run reports again.
        hub.Harness.End(submission.Id, RunOutcome.Failed);

        // Unmoved: done is terminal, and the second report is a no-op rather than an attempt to
        // leave it.
        Assert.Equal(SubmissionState.Done, submission.State);
    }

    [Fact]
    [Trait("req", "ACCESS-002")]
    public async Task Report_CarriesNothingBeyondTheState()
    {
        var submission = await Accepted();
        hub.Harness.ReportIn(submission.Id);

        var json = JsonSerializer.SerializeToElement(SubmissionView.Of(submission));

        // No run identifier, step, reasoning, duration, cost or history — ACCESS-002 says "and no
        // further detail" about the run, and OUT-02 owns everything more. What is here besides the
        // state are facts about the submission itself (ACCESS-004, contracts/hub-http-api.md).
        Assert.Equal(
            ["id", "state", "submittedAt", "excerpt"],
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

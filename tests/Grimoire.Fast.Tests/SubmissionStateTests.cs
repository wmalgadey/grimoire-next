using System.Text.Json;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The four states and the transitions of data-model.md (RUNS-001), and the hub-side half of
/// ACCESS-005: the response carries the state, and for a submission that has a run its model and its
/// two figures.
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
        Assert.Throws<InvalidOperationException>(
            () => hub.Board.Ended(submission.Id, SubmissionState.Done, tokensUsed: 0, toolCalls: 0, entriesLost: 0));
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
    [Trait("req", "ACCESS-003")]
    public async Task Report_OffersTheAcknowledgement_OnlyWhereAFailureIsUnacknowledged()
    {
        var submission = await Accepted();

        // Not failed: the control is not offered, and the field is not there at all.
        Assert.DoesNotContain("awaitingAcknowledgement", Reported(submission).EnumerateObject().Select(p => p.Name));

        hub.Harness.End(submission.Id, RunOutcome.Failed);

        // Failed and unacknowledged: this is the row that holds the queue.
        Assert.True(Reported(submission).GetProperty("awaitingAcknowledgement").GetBoolean());

        await hub.AcknowledgeAsync(submission.Id);

        // Acknowledged: still failed, and the control is gone on the next poll (RUNS-003).
        var afterwards = Reported(submission);
        Assert.Equal("failed", afterwards.GetProperty("state").GetString());
        Assert.DoesNotContain("awaitingAcknowledgement", afterwards.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Report_CarriesNoRunIdentifier_WhileAFailureIsUnacknowledged()
    {
        var submission = await Accepted();
        hub.Harness.End(submission.Id, RunOutcome.Failed);

        // No requirement id: with ACCESS-002 retired, nothing requires this any more. It holds because
        // nothing needs a run identifier — the acknowledgement and the record endpoint both address
        // the submission, which has exactly one run (INGEST-002) — so it is a design property, and a
        // test of one carries no id (Constitution IV.3, tests/README.md).
        var reported = Reported(submission);
        Assert.NotNull(submission.RunId);
        Assert.DoesNotContain(
            submission.RunId!.Value.ToString(),
            reported.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("req", "ACCESS-005")]
    public async Task Report_CarriesTheModelAndBothFigures_ForASubmissionThatHasARun()
    {
        var submission = await Accepted();
        hub.Harness.ReportIn(submission.Id);
        hub.Harness.Spend(submission.Id, 148_233);
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");

        var reported = Reported(submission);

        Assert.Equal("running", reported.GetProperty("state").GetString());
        Assert.Equal(FastHub.Model, reported.GetProperty("model").GetString());
        Assert.Equal(148_233, reported.GetProperty("tokensUsed").GetInt64());
        Assert.Equal(1, reported.GetProperty("toolCalls").GetInt32());
    }

    [Fact]
    [Trait("req", "ACCESS-005")]
    public async Task Report_CarriesNoRunFieldsAtAll_ForASubmissionThatHasNoRun()
    {
        // The first submission's run holds the queue, so the second one is accepted and waits its
        // turn — which is a submission with no run (RUNS-002).
        var running = await Accepted("Ada Lovelace wrote the first program.");
        var waiting = (await hub.SubmitAsync("Grace Hopper found the first bug.")).Accepted!;

        Assert.NotNull(hub.Conductor.Of(running.Id));
        Assert.Null(waiting.RunId);

        // Not zeros. Zeros would claim a run that spent nothing rather than no run at all, and the
        // browser would have a rule to apply where the response should simply say nothing
        // (contracts/hub-http-api.md).
        Assert.Equal(
            ["id", "state", "submittedAt", "excerpt"],
            Reported(waiting).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    [Trait("req", "ACCESS-005")]
    [Trait("req", "RUNS-007")]
    public async Task Report_SaysEntriesAreLost_OnlyWhereTheRecordCouldNotHoldSomething()
    {
        var submission = await Accepted();
        hub.Harness.ReportIn(submission.Id);

        // Nothing lost: the field is not there at all, so a row says lines are missing only when they
        // are (RUNS-007).
        Assert.DoesNotContain("entriesLost", Reported(submission).EnumerateObject().Select(p => p.Name));

        hub.Record.FailWrites = true;
        hub.Harness.Called(submission.Id, "read_page", """{"path":"ada.md"}""");

        Assert.Equal(1, Reported(submission).GetProperty("entriesLost").GetInt32());
    }

    private static JsonElement Reported(Submission submission) =>
        JsonSerializer.SerializeToElement(SubmissionView.Of(submission));

    [Theory]
    [InlineData(SubmissionState.Submitted, "submitted")]
    [InlineData(SubmissionState.Running, "running")]
    [InlineData(SubmissionState.Done, "done")]
    [InlineData(SubmissionState.Failed, "failed")]
    [Trait("req", "ACCESS-005")]
    public void Report_NamesEachStateAsOneOfTheFour(SubmissionState state, string wire) =>
        Assert.Equal(wire, SubmissionView.WireNameOf(state));
}

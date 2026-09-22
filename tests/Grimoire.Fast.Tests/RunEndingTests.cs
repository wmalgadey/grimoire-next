using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// Where a run ends, and what the verdict is taken from (RUNS-005,
/// <c>contracts/agent-cli-protocol.md</c>).
/// </summary>
/// <remarks>
/// A run ends at its process's exit or at the interrupt, never at the <c>result</c> message. The
/// result says what the agent did and the log says whether it recorded it, but neither is the
/// ending: the CLI reads stdin for as long as it is open, so the hub sends nothing further, the
/// process ends, and the exit code joins the other two. All three have to agree.
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "RUNS-005")]
public sealed class RunEndingTests
{
    private static async Task<(FastHub Hub, Submission Submission)> ARunThatWroteItsEntryAsync()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        await hub.Wiki.AppendLogAsync(
            $"Run {hub.Conductor.Of(submission.Id)!.Id} added one page.\n", TestContext.Current.CancellationToken);

        return (hub, submission);
    }

    [Fact]
    public async Task Run_EndsDone_WhenTheResultTheEntryAndAZeroExitAllAgree()
    {
        var (hub, submission) = await ARunThatWroteItsEntryAsync();

        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(SubmissionState.Done, submission.State);
    }

    [Fact]
    public async Task Run_EndsFailed_WhenTheResultWasCleanAndTheProcessExitedNonZero()
    {
        var (hub, submission) = await ARunThatWroteItsEntryAsync();
        hub.Harness.ExitCode = 1;

        // The agent said it was finished and the log names the run, so everything the hub was
        // told reads done. Then the process exited non-zero, which says the CLI did not do what
        // it reported doing. The protocol's decision table has a row for exactly this, and
        // reading the result alone would have missed it.
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Fact]
    public async Task Run_DoesNotEndAtTheResult_ButAtTheExitThatFollowsIt()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);
        await hub.Wiki.AppendLogAsync(
            $"Run {hub.Conductor.Of(submission.Id)!.Id} added one page.\n", TestContext.Current.CancellationToken);

        var run = hub.Conductor.Of(submission.Id)!;

        // Nothing further is sent, which is what lets the process end at all — and until it has,
        // the run is still the hub's.
        Assert.Equal(SubmissionState.Running, submission.State);

        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal([run.Id], hub.Harness.ToldNothingFurther);
    }

    [Fact]
    public async Task Run_EndsFailed_WhenTheProcessExitsWithNoResultAtAll()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);

        // A process that died mid-turn: no result, nothing in the log, and a run that would read
        // running for ever if the exit did not end it.
        hub.Harness.Exit(submission.Id, exitCode: 0);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }

    [Fact]
    public async Task Run_EndsFailed_WhenTheEntryIsStillMissingAfterTheNudge()
    {
        var hub = new FastHub();
        var submission = await hub.AcceptedAsync();
        hub.Harness.ReportIn(submission.Id);
        var run = hub.Conductor.Of(submission.Id)!;

        // No entry and not yet nudged: the agent is told once and carries on, and the run is not
        // over (RUNS-005).
        await hub.Harness.StoppedAsync(submission.Id);
        Assert.Equal([run.Id], hub.Harness.Nudged);
        Assert.Equal(SubmissionState.Running, submission.State);

        // Told once already, and the entry is still missing.
        await hub.Harness.StoppedAsync(submission.Id);

        Assert.Equal(SubmissionState.Failed, submission.State);
    }
}

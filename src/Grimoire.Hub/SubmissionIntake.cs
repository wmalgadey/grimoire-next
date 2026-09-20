using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// What happens when a text is submitted: the board decides, and an accepted submission is
/// dispatched as a run before the call returns (INGEST-001).
/// </summary>
/// <remarks>
/// This is where the two contexts meet, which is why it sits in the hub — the composition root is
/// the only project that knows all three (plan.md, Structure Decision). RUNS decides whether a
/// text is accepted and holds its state; GUARD runs it.
/// </remarks>
public sealed class SubmissionIntake(SubmissionBoard board, IAgentHarness harness)
{
    public async Task<SubmissionResult> SubmitAsync(
        string text,
        StartUpInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var result = board.Accept(text, inputs);

        if (result.Accepted is not { } submission)
        {
            // A refused submission is stored nowhere, carries no state, and starts no run.
            return result;
        }

        // A fresh identifier per run, distinct from the submission's: one submission has one run
        // here, but a run is its own thing — it names itself in the wiki's log and addresses its
        // own tool endpoint (data-model.md §Run).
        var dispatch = new AgentDispatch(Guid.NewGuid(), submission.Id, submission.Text);

        await harness.DispatchAsync(dispatch, Report(), cancellationToken).ConfigureAwait(false);

        // The run is under way and the call returns; the user waits for none of it.
        return result;
    }

    private RunReport Report() => new(
        AgentReportedIn: submissionId => board.Find(submissionId)?.AgentReportedIn(),
        RunEnded: (submissionId, outcome) => board.Find(submissionId)?.Ended(
            outcome == RunOutcome.Done ? SubmissionState.Done : SubmissionState.Failed));
}

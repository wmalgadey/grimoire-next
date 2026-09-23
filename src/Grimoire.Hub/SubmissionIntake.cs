using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// What happens when a text is submitted: the board decides, and an accepted submission is
/// dispatched as a run before the call returns (INGEST-001).
/// </summary>
/// <remarks>
/// This is where the contexts meet, which is why it sits in the hub — the composition root is the
/// only project that knows all three (plan.md, Structure Decision). RUNS decides whether a text is
/// accepted and holds its state; GUARD runs it; WIKI is reached only through the granted tools.
/// </remarks>
public sealed class SubmissionIntake(
    SubmissionBoard board,
    IAgentHarness harness,
    RunConductor conductor,
    SubmissionIntake.PromptAssembly assemblePrompt,
    string model)
{
    /// <summary>
    /// What a run is given, assembled from the instruction and the purpose description. The hub's
    /// <see cref="InstructionLoader"/> is what does this; nothing else puts text into the prompt
    /// (Constitution V.1).
    /// </summary>
    public delegate string PromptAssembly(string text, Guid runId);

    /// <summary>
    /// Accept a text and put its run under way, or refuse it.
    /// </summary>
    /// <remarks>
    /// There is deliberately no cancellation token. A run outlives the request that started it —
    /// that is what INGEST-001 means by accepting a submission without the user waiting for the
    /// run to end — so tying the dispatch to the caller's token would let a closed browser tab
    /// kill the run.
    /// </remarks>
    public async Task<SubmissionResult> SubmitAsync(string text, StartUpInputs inputs)
    {
        var result = board.Accept(text, inputs);

        if (result.Accepted is not { } submission)
        {
            // A refused submission is stored nowhere, carries no state, and starts no run.
            return result;
        }

        var run = conductor.Begin(submission.Id);

        try
        {
            // Assembling the prompt is inside this try and not above it. It reads the instruction
            // from disk, so it can throw for a reason that has nothing to do with the run — a file
            // deleted between start-up and now — and the run is already registered by then.
            var dispatch = new AgentDispatch(
                run.Id,
                submission.Id,
                assemblePrompt(submission.Text, run.Id),
                run.Grant,
                model);

            await harness.DispatchAsync(dispatch, conductor.Report(), CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A run that never began must not leave its submission reading Submitted: the board
            // counts that as a run in progress, so every later text would be refused for as long
            // as the process lives (INGEST-001, RUNS-002). It ended, and it ended failed.
            conductor.Report().RunEnded(submission.Id, RunOutcome.Failed);
            throw;
        }

        // The run is under way and the call returns; the user waits for none of it.
        return result;
    }
}

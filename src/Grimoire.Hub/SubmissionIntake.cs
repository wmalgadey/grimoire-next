using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// What happens when a text is submitted: the board decides whether it is accepted, and the queue
/// is then asked for the next run (INGEST-001, RUNS-002).
/// </summary>
/// <remarks>
/// This is where the contexts meet, which is why it sits in the hub — the composition root is the
/// only project that knows all three (plan.md, Structure Decision). RUNS decides whether a text is
/// accepted and holds its state; GUARD runs it; WIKI is reached only through the granted tools.
/// </remarks>
public sealed class SubmissionIntake(SubmissionBoard board, RunQueue queue)
{
    /// <summary>
    /// Accept a text, or refuse it. An accepted text is not necessarily the one that runs next: a
    /// run already under way keeps its place, and this submission waits its turn (RUNS-002).
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

        if (result.Accepted is null)
        {
            // A refused submission is stored nowhere, carries no state, and starts no run.
            return result;
        }

        // An accepted submission is one of the four events that can let a run start (research.md
        // R-03). The pump returns once the run it started is under way, and the user waits for no
        // more than that.
        await queue.PumpAsync().ConfigureAwait(false);

        return result;
    }
}

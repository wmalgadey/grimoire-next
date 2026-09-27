using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// What happens when a question is asked: the board decides whether it is accepted, it joins the
/// chat, and the queue is then asked for the next run (QUERY-001, QUERY-003, RUNS-002).
/// </summary>
/// <remarks>
/// The shape <see cref="SubmissionIntake"/> already has, because a question queues by the same rule.
/// This is where the contexts meet, which is why it sits in the hub — the composition root is the
/// only project that knows all three (plan.md, Structure Decision): RUNS decides whether a question is
/// accepted and holds its state, GUARD runs it under a grant that cannot write, and the chat it is read
/// in is the hub's.
/// </remarks>
public sealed class ChatIntake(RunBoard board, Chat chat, RunQueue queue)
{
    /// <summary>
    /// Accept a question, or refuse it. An accepted question is not necessarily the one that runs
    /// next: a run already under way keeps its place, and this question waits its turn (RUNS-002).
    /// </summary>
    /// <remarks>
    /// There is deliberately no cancellation token, for the reason <see cref="SubmissionIntake"/> has
    /// none: a run outlives the request that started it — that is what QUERY-001 means by accepting a
    /// question without the user waiting for its answer — so tying the dispatch to the caller's token
    /// would let a closed browser tab kill the run.
    /// </remarks>
    public async Task<QuestionResult> AskAsync(string text, StartUpInputs inputs)
    {
        var result = board.Ask(text, inputs);

        if (result.Accepted is not { } question)
        {
            // A refused question is stored nowhere, carries no state, joins no chat and starts no run
            // (QUERY-003). There is nothing of it afterwards to have been refused.
            return result;
        }

        // On the chat before the queue is pumped, so that the question is in the chat by the time
        // anything about its run can be reported into it — and before the user is answered, so the
        // turn they are given is one every browser reading the chat has too (ACCESS-007).
        chat.Ask(question);

        // An accepted question is one of the events that can let a run start (research.md R-03). The
        // pump returns once the run it started is under way, and the user waits for no more than that.
        await queue.PumpAsync().ConfigureAwait(false);

        return result;
    }
}

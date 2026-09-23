using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// What asks the board for the next run and dispatches it (RUNS-002).
/// </summary>
/// <remarks>
/// <para>
/// It judges nothing. Whether a run may start and which submission is next are decisions of the
/// RUNS context, made in <see cref="SubmissionBoard.TakeNext"/>; this is where the contexts meet,
/// which is why it sits in the hub beside <see cref="SubmissionIntake"/> (plan.md, Structure
/// Decision).
/// </para>
/// <para>
/// There is no timer, no background service and no scheduler. The queue is pumped by the events
/// that can unblock it and by nothing else — a submission was accepted, a run ended, a failure was
/// acknowledged, the hub started — and a waiting submission carries no timeout, so no clock could
/// make one start (research.md R-03).
/// </para>
/// </remarks>
public sealed class RunQueue(
    SubmissionBoard board,
    RunConductor conductor,
    IAgentHarness harness,
    RunQueue.PromptAssembly assemblePrompt,
    string model)
{
    /// <summary>
    /// What a run is given, assembled from the instruction and the purpose description. The hub's
    /// <see cref="InstructionLoader"/> is what does this; nothing else puts text into the prompt
    /// (Constitution V.1).
    /// </summary>
    public delegate string PromptAssembly(string text, Guid runId);

    private readonly Lock gate = new();
    private bool pumping;
    private bool askedAgain;

    /// <summary>
    /// Start whatever may start, and keep starting until nothing may.
    /// </summary>
    /// <remarks>
    /// One pump at a time. A run that ends while this is running asks again from the harness's own
    /// thread, and letting that call in would nest a dispatch inside a dispatch — a run whose
    /// dispatch fails ends immediately, so the nesting would be as deep as the queue is long.
    /// The request is not dropped either: it is remembered, and the loop below goes round once more
    /// for it. Dropping it would strand a waiting submission behind a run that ended in the moment
    /// between the last ask and this returning.
    /// </remarks>
    public async Task PumpAsync()
    {
        lock (gate)
        {
            if (pumping)
            {
                askedAgain = true;
                return;
            }

            pumping = true;
        }

        try
        {
            do
            {
                lock (gate)
                {
                    askedAgain = false;
                }

                await StartWhatIsWaitingAsync().ConfigureAwait(false);
            }
            while (AskedAgain());
        }
        finally
        {
            lock (gate)
            {
                pumping = false;
            }
        }
    }

    private bool AskedAgain()
    {
        lock (gate)
        {
            return askedAgain;
        }
    }

    /// <summary>
    /// Ask the board until it hands out nothing. Normally that is once: the run just started is
    /// itself what stops the next one. It goes round again where a dispatch could not start at all,
    /// because that run has already ended and the one behind it may now go.
    /// </summary>
    private async Task StartWhatIsWaitingAsync()
    {
        while (true)
        {
            // The run's identifier is made here and given to the board, so that the submission is
            // marked with the very run it is about to be dispatched to: a submission handed out
            // and a run begun are one step, not two (research.md R-04).
            var runId = Guid.NewGuid();

            if (board.TakeNext(runId) is not { } next)
            {
                return;
            }

            await StartAsync(next, runId).ConfigureAwait(false);
        }
    }

    private async Task StartAsync(Submission submission, Guid runId)
    {
        var run = conductor.Begin(submission.Id, runId);

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
        catch (Exception)
        {
            // A run that never began must not leave its submission under way: the board counts one
            // as a run in progress, so nothing behind it would ever start again. It ended, and it
            // ended failed — which is what the browser then shows for it.
            //
            // The throw stops here rather than reaching whoever pumped. With a queue, the
            // submission being dispatched is not necessarily the one just submitted, and answering
            // one user's request with another submission's failure would say something untrue
            // about theirs (RUNS-002).
            conductor.Report().RunEnded(submission.Id, RunOutcome.Failed);
        }
    }
}

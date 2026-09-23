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
    private bool closed;

    /// <summary>
    /// No further run starts, whatever asks. Called as the hub goes down, before the runs under way
    /// are stopped (RUNS-006).
    /// </summary>
    /// <remarks>
    /// Without this, stopping is a race it can lose: a run ends because it was stopped, that ending
    /// pumps the queue, and a waiting submission is dispatched behind the shutdown — an agent
    /// started by a Grimoire that is already leaving, and so one nothing will ever stop. Closed
    /// here and never reopened: a process that has begun to stop does not resume.
    /// </remarks>
    public void StopStartingRuns()
    {
        lock (gate)
        {
            closed = true;
        }
    }

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
    /// <para>
    /// Reading that request and giving up the pump are <b>one</b> critical section. Apart, a run
    /// ending in between would set a flag this pump has already read and the next pump would never
    /// be started — the same stranding, moved one step later, and with no event left to undo it.
    /// </para>
    /// </remarks>
    public async Task PumpAsync()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            if (pumping)
            {
                askedAgain = true;
                return;
            }

            pumping = true;
        }

        try
        {
            while (true)
            {
                await StartWhatIsWaitingAsync().ConfigureAwait(false);

                lock (gate)
                {
                    if (!askedAgain || closed)
                    {
                        pumping = false;
                        return;
                    }

                    askedAgain = false;
                }
            }
        }
        catch (Exception)
        {
            // The pump is given up on the way out too. Held, it would be held for the life of the
            // process, and no later event could start anything again.
            lock (gate)
            {
                pumping = false;
            }

            throw;
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
            // The board decides, and asks the conductor for the run only once it has: handing a
            // submission out, marking it with that run and recording both are one step under one
            // lock, so a stop between deciding and recording cannot exist (research.md R-04).
            if (board.TakeNext(conductor.Begin) is not { } run)
            {
                return;
            }

            await StartAsync(run).ConfigureAwait(false);
        }
    }

    private async Task StartAsync(Run run)
    {
        try
        {
            if (board.TextOf(run.SubmissionId) is not { } text)
            {
                return;
            }

            // Assembling the prompt is inside this try and not above it. It reads the instruction
            // from disk, so it can throw for a reason that has nothing to do with the run — a file
            // deleted between start-up and now — and the run is already registered by then.
            var dispatch = new AgentDispatch(
                run.Id,
                run.SubmissionId,
                assemblePrompt(text, run.Id),
                run.Grant,
                model);

            await harness.DispatchAsync(dispatch, conductor.Report(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A dispatch does not fail only before its agent exists: the adapter starts the
            // process and records it, and the prompt written to it afterwards can still fault. So
            // the agent is stopped before the run is ended — nothing is left watching such a
            // process, because the reader that would have reported its exit was never started, and
            // an agent left alive would hold the granted tools with no ceiling on it (RUNS-006).
            try
            {
                await harness.StopAsync(run.Id, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Whether the stop reached anything or not, the run is over and the ending below
                // has to happen: a submission left under way strands everything behind it.
            }

            // A run that never began must not leave its submission under way: the board counts one
            // as a run in progress, so nothing behind it would ever start again. It ended, and it
            // ended failed — which is what the browser then shows for it.
            //
            // The throw stops here rather than reaching whoever pumped. With a queue, the
            // submission being dispatched is not necessarily the one just submitted, and answering
            // one user's request with another submission's failure would say something untrue
            // about theirs (RUNS-002).
            conductor.Report().RunEnded(run.SubmissionId, RunOutcome.Failed);
        }
    }
}

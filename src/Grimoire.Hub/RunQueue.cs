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
    RunQueue.PromptAssembly assemblePrompt)
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
    /// <para>
    /// Without this, stopping is a race it can lose: a run ends because it was stopped, that ending
    /// pumps the queue, and a waiting submission is dispatched behind the shutdown — an agent
    /// started by a Grimoire that is already leaving, and so one nothing will ever stop. Closed
    /// here and never reopened: a process that has begun to stop does not resume.
    /// </para>
    /// <para>
    /// Closing is only half of it. A pump already past its own check may hold a submission the
    /// board has handed out, and ending that admission is <see cref="DrainAsync"/>'s: the caller
    /// closes, drains, and only then stops what is under way
    /// (<see cref="HubApplication.StopEverythingAsync"/>).
    /// </para>
    /// </remarks>
    public void StopStartingRuns()
    {
        lock (gate)
        {
            closed = true;
        }
    }

    /// <summary>
    /// Wait until no pump is between taking a submission and dispatching it.
    /// </summary>
    /// <remarks>
    /// Called after <see cref="StopStartingRuns"/> and before anything is stopped. Without it,
    /// shutdown can drain the runs it can see while a pump is still holding one the board handed
    /// out a moment earlier — and that one is then dispatched into a hub that has finished
    /// stopping, with no conductor watching it and no ceiling on it (RUNS-006). A pump that is
    /// still running here either dispatches its run, which puts it where the drain will find it,
    /// or finds the queue closed and ends it without an agent.
    /// </remarks>
    public async Task DrainAsync()
    {
        while (Pumping)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        }
    }

    private bool Pumping
    {
        get
        {
            lock (gate)
            {
                return pumping;
            }
        }
    }

    private bool Closed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
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

            // Asked again after the board has handed one out, because the hub may have begun to
            // stop in between. Dispatching now would start an agent into a hub that is leaving:
            // the drain above waits for this pump, but what it must find here is a run that was
            // never given a process, not one that was (RUNS-006).
            if (Closed)
            {
                // Grimoire is leaving, so this run ends with it and its record says so — the same
                // reason a run that was under way at the stop reads after a restart (RUNS-004,
                // RUNS-006).
                conductor.Report().RunEnded(
                    run.SubmissionId, RunOutcome.Failed, RunEndedBecause.GrimoireStopped);
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
            // The model comes off the run, which was given it at Begin beside the grant and both
            // ceilings. One fewer place the model lives, not one more (data-model.md §Run).
            var dispatch = new AgentDispatch(
                run.Id,
                run.SubmissionId,
                assemblePrompt(text, run.Id),
                run.Grant,
                run.Model);

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
                // Whether the stop reached anything or not, the ending below has to happen: a
                // submission left under way strands everything behind it, which is a certain
                // failure weighed against a rare one.
                //
                // The rare one is admitted rather than argued away: a dispatch that started a
                // child and then failed, whose stop also failed, leaves an agent alive while this
                // run reads failed. What catches it is the backstop RUNS-006 already defines —
                // the child's identity is recorded before the prompt is written to it, so the next
                // start-up terminates it before anything else runs. Holding the queue here
                // instead would trade that bounded window for a Grimoire that stops working
                // whenever a stop fails once.
            }

            // A run that never began must not leave its submission under way: the board counts one
            // as a run in progress, so nothing behind it would ever start again. It ended, and it
            // ended failed — which is what the browser then shows for it.
            //
            // The throw stops here rather than reaching whoever pumped. With a queue, the
            // submission being dispatched is not necessarily the one just submitted, and answering
            // one user's request with another submission's failure would say something untrue
            // about theirs (RUNS-002).
            // A dispatch that never gave the run a process, or one that failed after it had: either
            // way no agent of this run is at work, which is the reason the record's tail names
            // (RUNS-008).
            conductor.Report().RunEnded(
                run.SubmissionId, RunOutcome.Failed, RunEndedBecause.AgentProcessDied);
        }
    }
}

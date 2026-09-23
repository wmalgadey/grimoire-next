using System.Collections.Concurrent;
using Grimoire.Agent;
using Grimoire.Runs;
using Grimoire.Wiki;

namespace Grimoire.Hub;

/// <summary>
/// What happens to a run while it is under way. Every judgment is made here — both ceilings, the
/// run's state, and RUNS-005's single nudge; the harness reports facts and acts on what it is told
/// (<c>contracts/agent-cli-protocol.md</c>).
/// </summary>
public sealed class RunConductor(
    SubmissionBoard board,
    IAgentHarness harness,
    IWikiStore wiki,
    TimeProvider clock,
    RunConductor.NextRunMayStart nextRunMayStart)
{
    /// <summary>
    /// A run has ended, so whatever was waiting behind it may now go. One of the four events that
    /// pump the queue (research.md R-03); the hub's <see cref="RunQueue"/> is what answers it.
    /// </summary>
    public delegate Task NextRunMayStart();

    private readonly ConcurrentDictionary<Guid, Watched> runs = new();

    /// <summary>A run under way, together with the timer that holds it to its elapsed ceiling.</summary>
    private sealed record Watched(Run Run, ITimer Deadline);

    /// <summary>
    /// A run for this submission, with its grant and both ceilings recorded on it. Called by the
    /// board, under its lock, at the moment it hands the submission out: the run it returns is the
    /// one the submission is marked with and the one recorded in the store (research.md R-04).
    /// </summary>
    public Run Begin(Guid submissionId)
    {
        var run = new Run(
            Guid.NewGuid(),
            submissionId,
            clock.GetUtcNow(),
            ToolGrant.Ingest(clock),
            Ceilings.Fixed);

        // GUARD-004's elapsed ceiling has to be able to fire while the agent says nothing at all —
        // a model call that hangs, or a tool call that never comes back, is exactly the run the
        // ceiling exists for, and such a run reports no cost to read the clock against. So it is
        // the clock that raises it here, and not a line of the CLI's.
        var deadline = clock.CreateTimer(
            _ => ElapsedCeilingReached(submissionId),
            state: null,
            dueTime: run.Ceilings.Elapsed,
            period: Timeout.InfiniteTimeSpan);

        runs[submissionId] = new Watched(run, deadline);
        return run;
    }

    /// <summary>The run this submission is being worked by, or null once it is over.</summary>
    public Run? Of(Guid submissionId) => runs.GetValueOrDefault(submissionId)?.Run;

    public RunReport Report() => new(
        AgentReportedIn: AgentReportedIn,
        CostSoFar: CostSoFar,
        AgentStopped: AgentStoppedAsync,
        AgentExited: AgentExited,
        RunEnded: RunEnded,
        AgentProcessIs: board.AgentProcessIs);

    /// <summary>
    /// The hub is going down, so the run under way goes with it: no agent goes on working on a run
    /// Grimoire has ended (RUNS-006).
    /// </summary>
    /// <remarks>
    /// Stopped exactly as a ceiling stops it — the interrupt first, the process kill behind it —
    /// because DEC-016 settled that mechanism with evidence and this is the same act at a different
    /// moment (Constitution II.1). The run then ends failed, which is what it reads after the
    /// restart too (RUNS-004).
    /// <para>
    /// Where the stop gives Grimoire no chance to act — a kill it cannot catch, a power cut — this
    /// never runs, and the agent outlives it until the next start-up terminates it by the identity
    /// recorded with its run (research.md R-05, R-11).
    /// </para>
    /// </remarks>
    public Task StopEverythingAsync() =>
        Task.WhenAll(runs.ToArray().Select(under => StopAtACeilingAsync(under.Value.Run, under.Key)));

    private void AgentReportedIn(Guid submissionId) => board.ReportedIn(submissionId);

    /// <summary>
    /// The cost ceiling, watched as the run spends. At either ceiling the run is stopped at once,
    /// a model call in flight included, and it ends failed (GUARD-004).
    /// </summary>
    private void CostSoFar(Guid submissionId, long tokensUsed)
    {
        if (runs.GetValueOrDefault(submissionId)?.Run is not { } run)
        {
            return;
        }

        // Recorded on the run, not only compared: the stop decision below reads it, and so does
        // the token ceiling when the run is asked how it ended (GUARD-004).
        run.Spent(tokensUsed);

        if (run.Ceilings.ReachedBy(clock.GetUtcNow() - run.StartedAt, run.TokensUsed))
        {
            _ = StopAtACeilingAsync(run, submissionId);
        }
    }

    /// <summary>
    /// The decision RUNS-005 rests on. The wiki's log is read for the run's identifier and nothing
    /// else in the wiki is read at all.
    /// </summary>
    /// <remarks>
    /// The run does not end here. What this settles is whether the agent is told anything further:
    /// one nudge, or nothing at all. A run ends at its process's exit or at the interrupt, and the
    /// verdict is taken there with the exit code in it.
    /// </remarks>
    private async Task AgentStoppedAsync(Guid submissionId, bool endedAbnormally)
    {
        if (runs.GetValueOrDefault(submissionId)?.Run is not { } run)
        {
            return;
        }

        var log = await wiki.ReadAsync(WikiFile.Log, CancellationToken.None).ConfigureAwait(false);

        var decision = run.AgentStopped(new AgentStop(
            run.IsNamedIn(log),
            clock.GetUtcNow() - run.StartedAt,
            run.TokensUsed,
            endedAbnormally));

        if (decision == RunDecision.Nudge)
        {
            await harness.NudgeAsync(run.Id, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        // Done or failed, nothing further is sent. The CLI reads stdin for as long as it is open,
        // so this is also what lets the agent's process end at all — and the run ends there.
        await harness.NothingFurtherAsync(run.Id, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Where a run ends. The result, the log entry and the exit code are read together, which is
    /// the protocol's decision table whole — its "or a non-zero exit" row included.
    /// </summary>
    private void AgentExited(Guid submissionId, int exitCode)
    {
        if (runs.GetValueOrDefault(submissionId)?.Run is not { } run)
        {
            return;
        }

        RunEnded(submissionId, run.Exited(exitCode, clock.GetUtcNow() - run.StartedAt));
    }

    /// <summary>
    /// A run ends once. Removing it is what makes that true: a harness whose process died after
    /// the hub had already ended the run reports again, and a submission that is already done or
    /// failed refuses to be moved. The second report is a no-op rather than a crash.
    /// </summary>
    /// <remarks>
    /// Whatever the run had already written stays in the wiki, in every failed case: nothing here
    /// reaches back into it (WIKI-003).
    /// </remarks>
    private void RunEnded(Guid submissionId, RunOutcome outcome)
    {
        if (!runs.TryRemove(submissionId, out var watched))
        {
            return;
        }

        watched.Deadline.Dispose();

        board.Ended(
            submissionId,
            outcome == RunOutcome.Done ? SubmissionState.Done : SubmissionState.Failed);

        // The queue moves. Nothing awaits this: a run ends on whatever thread the harness reads
        // on, and the run being reported is over either way — what happens behind it is the
        // queue's, and it cannot fail in a way this caller could answer for (RUNS-002).
        _ = nextRunMayStart();
    }

    /// <summary>
    /// The elapsed ceiling, raised by the clock rather than by anything the agent said. The run is
    /// stopped the same way the cost ceiling stops it — the interrupt first, the kill behind it —
    /// and then it ends failed (GUARD-004).
    /// </summary>
    /// <remarks>
    /// It ends here rather than waiting for the agent's <c>result</c>, which is what the cost
    /// ceiling can afford to do: a run that has just reported its cost is talking to us, whereas a
    /// run that reached this ceiling may be one that has stopped talking altogether. The verdict is
    /// not in doubt either way — a run at a ceiling ends failed — and <see cref="RunEnded"/> is a
    /// no-op if the stop got the agent to report after all.
    /// </remarks>
    private void ElapsedCeilingReached(Guid submissionId)
    {
        if (runs.GetValueOrDefault(submissionId)?.Run is not { } run)
        {
            return;
        }

        _ = StopAtACeilingAsync(run, submissionId);
    }

    /// <summary>
    /// What either ceiling does: the interrupt first, and then the ending. Both go through here so
    /// that neither can stop a run without also ending it — a stop that the agent does not answer
    /// would otherwise leave the submission reading running, and the board refuses every later
    /// text while one does (GUARD-004, RUNS-002).
    /// </summary>
    private async Task StopAtACeilingAsync(Run run, Guid submissionId)
    {
        try
        {
            await harness.StopAsync(run.Id, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Nothing awaits this, so a throw would surface later as an unobserved task exception
            // and the run would be left reading running. Whether the stop reached the agent or
            // not, the ceiling was reached and the run is over.
        }
        finally
        {
            RunEnded(submissionId, RunOutcome.Failed);
        }
    }
}

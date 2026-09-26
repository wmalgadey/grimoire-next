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
    IRunRecord record,
    TimeProvider clock,
    string model,
    RunConductor.NextRunMayStart nextRunMayStart)
{
    /// <summary>
    /// A run has ended, so whatever was waiting behind it may now go. One of the four events that
    /// pump the queue (research.md R-03); the hub's <see cref="RunQueue"/> is what answers it.
    /// </summary>
    public delegate Task NextRunMayStart();

    private readonly ConcurrentDictionary<Guid, Watched> runs = new();

    /// <summary>
    /// A run under way: the run, the timer that holds it to its elapsed ceiling, and the one lock every
    /// report about it is taken under.
    /// </summary>
    /// <param name="Gate">
    /// What makes a report and the ending mutually exclusive. Everything the hub learns about a run
    /// arrives on whatever thread the harness reads on, and every callback here used to read the run
    /// and then act on it in two steps — so a moment could be appended to the record and counted on
    /// either side of an ending that happened in between, leaving the row and the record disagreeing
    /// about what the run did. Under this lock a report falls <b>entirely</b> before the ending or
    /// entirely after it, and one that falls after finds no run and does nothing.
    /// <para>
    /// Always the outermost lock taken here. The record's and the board's are taken inside it and
    /// never the other way about, which is what keeps the three from making a cycle.
    /// </para>
    /// </param>
    private sealed record Watched(Run Run, ITimer Deadline, Lock Gate);

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
            Ceilings.Fixed,
            model);

        // The head before the agent. This runs inside the board's lock, at the moment the submission
        // is handed out and before the dispatch — so a run whose dispatch fails still has a record,
        // and its tail says the agent's process died (RUNS-007, contracts/run-record.md).
        record.Begin(new RunFrameHead(
            run.Id,
            run.SubmissionId,
            run.Model,
            run.Grant.ToolNames,
            run.Grant.RecordedAt,
            run.Ceilings,
            run.StartedAt));

        // GUARD-004's elapsed ceiling has to be able to fire while the agent says nothing at all —
        // a model call that hangs, or a tool call that never comes back, is exactly the run the
        // ceiling exists for, and such a run reports no cost to read the clock against. So it is
        // the clock that raises it here, and not a line of the CLI's.
        var deadline = clock.CreateTimer(
            _ => ElapsedCeilingReached(submissionId),
            state: null,
            dueTime: run.Ceilings.Elapsed,
            period: Timeout.InfiniteTimeSpan);

        runs[submissionId] = new Watched(run, deadline, new Lock());
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
        AgentProcessIs: board.AgentProcessIs,
        MomentHappened: MomentHappened);

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
    /// <remarks>
    /// Drained rather than snapshotted once. Stopping a run ends it, and an ending is one of the
    /// events that pump the queue — so with admission still open a run could be dispatched behind
    /// the snapshot and outlive the hub. Admission is closed first, by
    /// <see cref="HubApplication.StopEverythingAsync"/>, and this then goes round until nothing is
    /// under way; every pass ends the runs it took, so it cannot go round for ever.
    /// </remarks>
    public async Task StopEverythingAsync()
    {
        while (runs.ToArray() is { Length: > 0 } underWay)
        {
            await Task.WhenAll(underWay.Select(u => StopAsync(u.Value.Run, u.Key, RunEndedBecause.GrimoireStopped)))
                .ConfigureAwait(false);
        }
    }

    private void AgentReportedIn(Guid submissionId) => board.ReportedIn(submissionId);

    /// <summary>
    /// The cost ceiling, watched as the run spends. At either ceiling the run is stopped at once,
    /// a model call in flight included, and it ends failed (GUARD-004).
    /// </summary>
    private void CostSoFar(Guid submissionId, long tokensUsed, IReadOnlyDictionary<string, ModelTokens> tokensPerModel)
    {
        if (Reporting(submissionId) is not { } watched)
        {
            return;
        }

        var run = watched.Run;
        TimeSpan elapsed;

        lock (watched.Gate)
        {
            if (HasEnded(submissionId))
            {
                return;
            }

            // Recorded on the run, not only compared: the stop decision below reads it, and so does
            // the token ceiling when the run is asked how it ended (GUARD-004). The breakdown comes
            // with it, and the record's tail says what each model spent (RUNS-008).
            run.Spent(tokensUsed, tokensPerModel);

            // The figures the list shows. `Spent` is a Math.Max, so this writes the store two to four
            // times a turn rather than once per streamed line (RUNS-010, research.md R-06).
            FiguresRose(run);

            elapsed = clock.GetUtcNow() - run.StartedAt;
        }

        // Outside the lock: stopping ends the run, and ending it takes this same lock.
        if (run.Ceilings.ReachedBy(elapsed, run.TokensUsed))
        {
            _ = StopAsync(run, submissionId, run.CeilingReachedBy(elapsed));
        }
    }

    /// <summary>
    /// One thing the run did, put in the record at the moment the hub read it (RUNS-009).
    /// </summary>
    /// <remarks>
    /// The clock is read here rather than in the adapter, because the clock is the hub's (DEC-018) and
    /// a record whose times an adapter stamped would be one the Fast suite could not drive. A tool
    /// call also raises the run's count, which is the second of the two figures the list shows
    /// (RUNS-010).
    /// </remarks>
    private void MomentHappened(Guid submissionId, TranscriptMoment moment)
    {
        if (Reporting(submissionId) is not { } watched)
        {
            return;
        }

        lock (watched.Gate)
        {
            // Read again inside the lock. The run may have ended while this callback waited for it,
            // and a moment that arrives after the ending belongs to neither the record nor the count:
            // accounted for on one side of the ending and not the other, the row and the record would
            // disagree about what the run did (RUNS-009, RUNS-010).
            if (HasEnded(submissionId))
            {
                return;
            }

            record.Append(RunMoment.Of(watched.Run.Id, clock.GetUtcNow(), moment));

            if (moment.Kind == RunMomentKind.ToolCalled)
            {
                watched.Run.ToolCalled();
                FiguresRose(watched.Run);
            }
        }
    }

    /// <summary>The run this report is about, or null once it is over.</summary>
    private Watched? Reporting(Guid submissionId) => runs.GetValueOrDefault(submissionId);

    /// <summary>Whether the run is already over. Read inside that run's lock.</summary>
    private bool HasEnded(Guid submissionId) => !runs.ContainsKey(submissionId);

    /// <summary>
    /// The run's figures as they now stand, offered to the board, which writes them only where one has
    /// actually changed (RUNS-010).
    /// </summary>
    /// <remarks>
    /// All three together, because one event writes them and they are read as one row: the tokens and
    /// the tool calls the run itself holds, and how many entries its record could not hold, which only
    /// the record knows. Read apart, the row could show a gap that belongs to another moment.
    /// </remarks>
    private void FiguresRose(Run run) =>
        board.RunFiguresAre(run.SubmissionId, run.TokensUsed, run.ToolCalls, record.EntriesLost(run.Id));

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

            // Appended by the hub, which knows it nudged. Never read back off the agent's stream:
            // measured, the CLI does not echo what is written to its stdin, so the nudge cannot
            // appear twice (RUNS-009, research.md R-03).
            record.Append(RunMoment.GrimoireSaid(
                run.Id, clock.GetUtcNow(), IAgentHarness.LogEntryMissing));
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

        var ending = run.Exited(exitCode, clock.GetUtcNow() - run.StartedAt);

        RunEnded(submissionId, ending.Outcome, ending.Because);
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
    private void RunEnded(Guid submissionId, RunOutcome outcome, RunEndedBecause because)
    {
        if (Reporting(submissionId) is not { } watched)
        {
            return;
        }

        lock (watched.Gate)
        {
            // Taken under the run's own lock, so a report already under way finishes first and every
            // report after this finds no run. Removing inside the lock is also what makes two endings
            // racing — a ceiling and an exit — end the run once.
            if (!runs.TryRemove(submissionId, out _))
            {
                return;
            }

            Finish(watched, outcome, because);
        }

        // The queue moves, outside the run's lock: what starts behind this one must not be started
        // while the run that ended is still being written down (RUNS-002).
        _ = nextRunMayStart();
    }

    /// <summary>
    /// The tail, the final figures and the terminal state, with the run already taken off the board of
    /// those under way. Assumes that run's lock.
    /// </summary>
    private void Finish(Watched watched, RunOutcome outcome, RunEndedBecause because)
    {
        watched.Deadline.Dispose();

        var run = watched.Run;

        // The tail where the verdict is taken, and with the final figures beside it. Written before
        // the board is told, so that a record is complete by the time the browser can read the row as
        // ended — the two are read by the same poll (RUNS-007, RUNS-008).
        record.Ended(new RunFrameTail(
            run.Id,
            clock.GetUtcNow(),
            outcome,
            because,
            clock.GetUtcNow() - run.StartedAt,
            run.TokensUsed,
            run.Ceilings,
            run.TokensPerModel));

        // The terminal state and the final figures in one pass of the board's lock. Told separately, a
        // poll landing between them would read `running` beside a final figure — and a tail whose write
        // just failed would raise the count of lost entries on a row still reading `running`
        // (ACCESS-005).
        board.Ended(
            run.SubmissionId,
            outcome == RunOutcome.Done ? SubmissionState.Done : SubmissionState.Failed,
            run.TokensUsed,
            run.ToolCalls,
            record.EntriesLost(run.Id));
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

        _ = StopAsync(run, submissionId, RunEndedBecause.TimeCeiling);
    }

    /// <summary>
    /// What either ceiling does, and what stopping Grimoire does: the interrupt first, and then the
    /// ending, for the reason the record's tail will give. All three go through here so that none of
    /// them can stop a run without also ending it — a stop that the agent does not answer would
    /// otherwise leave the submission reading running, and the board refuses every later text while
    /// one does (GUARD-004, RUNS-002, RUNS-006).
    /// </summary>
    private async Task StopAsync(Run run, Guid submissionId, RunEndedBecause because)
    {
        try
        {
            await harness.StopAsync(run.Id, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Nothing awaits this, so a throw would surface later as an unobserved task exception
            // and the run would be left reading running. Whether the stop reached the agent or
            // not, what made us stop it happened and the run is over.
        }
        finally
        {
            RunEnded(submissionId, RunOutcome.Failed, because);
        }
    }
}

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
public sealed class RunConductor(SubmissionBoard board, IAgentHarness harness, IWikiStore wiki, TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, Run> runs = new();

    /// <summary>A run for this submission, with its grant and both ceilings recorded on it.</summary>
    public Run Begin(Guid submissionId)
    {
        var run = new Run(
            Guid.NewGuid(),
            submissionId,
            clock.GetUtcNow(),
            ToolGrant.Ingest(clock),
            Ceilings.Fixed);

        runs[submissionId] = run;
        return run;
    }

    /// <summary>The run this submission is being worked by, or null once it is over.</summary>
    public Run? Of(Guid submissionId) => runs.GetValueOrDefault(submissionId);

    public RunReport Report() => new(
        AgentReportedIn: AgentReportedIn,
        CostSoFar: CostSoFar,
        AgentStopped: AgentStoppedAsync,
        RunEnded: RunEnded);

    private void AgentReportedIn(Guid submissionId) => board.Find(submissionId)?.AgentReportedIn();

    /// <summary>
    /// The cost ceiling, watched as the run spends. At either ceiling the run is stopped at once,
    /// a model call in flight included, and it ends failed (GUARD-004).
    /// </summary>
    private void CostSoFar(Guid submissionId, long tokensUsed)
    {
        if (runs.GetValueOrDefault(submissionId) is not { } run)
        {
            return;
        }

        if (run.Ceilings.ReachedBy(clock.GetUtcNow() - run.StartedAt, tokensUsed))
        {
            _ = harness.StopAsync(run.Id, CancellationToken.None);
        }
    }

    /// <summary>
    /// The decision RUNS-005 rests on. The wiki's log is read for the run's identifier and nothing
    /// else in the wiki is read at all.
    /// </summary>
    private async Task AgentStoppedAsync(Guid submissionId, bool endedAbnormally)
    {
        if (runs.GetValueOrDefault(submissionId) is not { } run)
        {
            return;
        }

        var log = await wiki.ReadAsync(WikiFile.Log, CancellationToken.None).ConfigureAwait(false);

        var decision = run.AgentStopped(new AgentStop(
            run.IsNamedIn(log),
            clock.GetUtcNow() - run.StartedAt,
            run.TokensUsed,
            endedAbnormally));

        switch (decision)
        {
            case RunDecision.Nudge:
                await harness.NudgeAsync(run.Id, CancellationToken.None).ConfigureAwait(false);
                break;

            case RunDecision.Done:
                RunEnded(submissionId, RunOutcome.Done);
                break;

            default:
                RunEnded(submissionId, RunOutcome.Failed);
                break;
        }
    }

    private void RunEnded(Guid submissionId, RunOutcome outcome)
    {
        // Whatever the run had already written stays in the wiki, in every failed case: nothing
        // here reaches back into it (WIKI-003).
        runs.TryRemove(submissionId, out _);

        board.Find(submissionId)?.Ended(
            outcome == RunOutcome.Done ? SubmissionState.Done : SubmissionState.Failed);
    }
}

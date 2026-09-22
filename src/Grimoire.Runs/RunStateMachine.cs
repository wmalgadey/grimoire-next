using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>What the hub knows at the moment the agent stopped.</summary>
/// <param name="LogEntryPresent">Whether the wiki's log holds an entry for this run.</param>
/// <param name="Elapsed">How long the run has been going, measured against <c>TimeProvider</c>.</param>
/// <param name="TokensUsed">Every token the run has caused (GUARD-004).</param>
/// <param name="EndedAbnormally">
/// The agent did not stop of its own accord — an aborted stream, or a process that exited
/// non-zero. RUNS-005 ends a run done only where the agent "stopped on its own".
/// </param>
public sealed record AgentStop(bool LogEntryPresent, TimeSpan Elapsed, long TokensUsed, bool EndedAbnormally = false);

/// <summary>
/// What the hub does about a run whose agent has stopped. None of these ends the run: a run ends
/// at its process's exit or at the interrupt, never at a <c>result</c>. What this settles is
/// whether anything further is sent to the agent.
/// </summary>
public enum RunDecision
{
    /// <summary>Nothing further is sent, and the log names the run. The verdict waits for the exit.</summary>
    Done,

    /// <summary>Tell the agent its log entry is missing and let it carry on. Once, and only once.</summary>
    Nudge,

    /// <summary>Nothing further is sent, and this run will not be done whatever the exit says.</summary>
    Failed,
}

/// <summary>
/// One attempt to work one submission into the wiki, and the decision RUNS-005 rests on.
/// </summary>
/// <remarks>
/// Every judgment about a run is made here, not by the agent and not by the CLI. A stop loses all
/// of this — the grant the run was given and the tokens it spent included — which the spec says
/// and the owner accepted until RUNS-004 (research.md R-08).
/// </remarks>
public sealed class Run
{
    public Run(Guid id, Guid submissionId, DateTimeOffset startedAt, ToolGrant grant, Ceilings ceilings)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(ceilings);

        Id = id;
        SubmissionId = submissionId;
        StartedAt = startedAt;
        Grant = grant;
        Ceilings = ceilings;
    }

    public Guid Id { get; }

    public Guid SubmissionId { get; }

    public DateTimeOffset StartedAt { get; }

    /// <summary>The tools this run was granted, recorded with it (GUARD-003).</summary>
    public ToolGrant Grant { get; }

    public Ceilings Ceilings { get; }

    /// <summary>Every token the run has caused so far (GUARD-004).</summary>
    public long TokensUsed { get; private set; }

    /// <summary>
    /// Whether the agent has already been told once that its log entry is missing. This exists for
    /// no other purpose than to make RUNS-005's single nudge decidable.
    /// </summary>
    public bool LogEntryNudged { get; private set; }

    /// <summary>Whether a <c>result</c> arrived and the agent stopped of its own accord.</summary>
    /// <remarks>
    /// Kept because the verdict is not taken where this is learnt. A run ends at its process's
    /// exit, and by then the result is behind it.
    /// </remarks>
    public bool StoppedOfItsOwnAccord { get; private set; }

    /// <summary>Whether the wiki's log named this run when the agent last stopped.</summary>
    public bool LogEntryWasPresent { get; private set; }

    /// <summary>Record what the run has caused so far. Never goes backwards.</summary>
    public void Spent(long tokensUsed) => TokensUsed = Math.Max(TokensUsed, tokensUsed);

    /// <summary>
    /// Whether the wiki's log holds an entry for this run.
    /// </summary>
    /// <remarks>
    /// The entry is present when <c>log.md</c> contains this run's identifier as plain text, and
    /// nothing else is parsed — not the entry's shape, not its prose, not which lines belong to
    /// which entry. WIKI-001 already asks the instruction to have each entry identify its run, so
    /// no format is imposed on the agent and the log stays a document a person reads. A run
    /// identifier is a fresh GUID, so it cannot turn up in an older entry.
    /// </remarks>
    public bool IsNamedIn(string? log) =>
        log is not null && log.Contains(Id.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The decision table of <c>contracts/agent-cli-protocol.md</c>, whole. Nothing but the log
    /// entry is read in the wiki to reach it (RUNS-005).
    /// </summary>
    public RunDecision AgentStopped(AgentStop stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        Spent(stop.TokensUsed);

        // Recorded rather than acted on: the run does not end here, and these are two of the three
        // things the verdict at the exit is taken from.
        StoppedOfItsOwnAccord = !stop.EndedAbnormally;
        LogEntryWasPresent = stop.LogEntryPresent;

        // A ceiling reached ends the run failed whatever the log says, and so does an ending the
        // agent did not choose (GUARD-004).
        if (stop.EndedAbnormally || Ceilings.ReachedBy(stop.Elapsed, stop.TokensUsed))
        {
            return RunDecision.Failed;
        }

        if (stop.LogEntryPresent)
        {
            return RunDecision.Done;
        }

        if (LogEntryNudged)
        {
            // Told once already, and the entry is still missing.
            return RunDecision.Failed;
        }

        LogEntryNudged = true;
        return RunDecision.Nudge;
    }

    /// <summary>
    /// The verdict, taken where a run actually ends: at its process's exit. Three things have to
    /// agree — the agent stopped of its own accord, the log names the run, and the process exited
    /// zero — and any one of them missing is a failed run. A ceiling reached fails it whatever the
    /// other three say (RUNS-005, GUARD-004, contracts/agent-cli-protocol.md).
    /// </summary>
    /// <remarks>
    /// The exit code is read here and nowhere else, which is what the protocol's "or a non-zero
    /// exit" row asks for. A CLI that reports a clean result and then exits non-zero did not do
    /// what it said it did, and the run is not done.
    /// </remarks>
    public RunOutcome Exited(int exitCode, TimeSpan elapsed) =>
        StoppedOfItsOwnAccord
        && LogEntryWasPresent
        && exitCode == 0
        && !Ceilings.ReachedBy(elapsed, TokensUsed)
            ? RunOutcome.Done
            : RunOutcome.Failed;
}

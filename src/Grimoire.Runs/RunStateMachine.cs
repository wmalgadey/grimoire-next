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

/// <summary>What the hub does about a run whose agent has stopped.</summary>
public enum RunDecision
{
    /// <summary>The run is over and it is done.</summary>
    Done,

    /// <summary>Tell the agent its log entry is missing and let it carry on. Once, and only once.</summary>
    Nudge,

    /// <summary>The run is over and it failed.</summary>
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

        TokensUsed = stop.TokensUsed;

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
}

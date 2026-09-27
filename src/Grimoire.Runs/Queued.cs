using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// Something the user handed to Grimoire that is waiting for a run, is being worked by one, or is
/// finished with one. <see cref="Submission"/> and <see cref="Question"/> are the two kinds.
/// </summary>
/// <remarks>
/// <para>
/// It holds <b>only what the queue rule consults</b> — waiting, under way, an unacknowledged
/// failure — and the run that answers those, because RUNS-002 orders waiting work across both kinds
/// by when it was made and <see cref="RunBoard"/> keeps one ordered list to carry that order
/// intrinsically (research.md R-03).
/// </para>
/// <para>
/// An abstraction is allowed here because <b>two real implementations exist</b> (Constitution II.4),
/// and what they do not share is the whole of the difference: a submission is persisted, carries an
/// excerpt of its text and appears in the browser's list; a question is none of those.
/// </para>
/// <para>
/// There is <b>no state field beyond the terminal one</b>. A submission's four states and a
/// question's four are both read from whether there is a run and whether it has ended, so the two
/// can never disagree with the queue rule that reads the same facts.
/// </para>
/// <para>
/// Every change here is made through the board and under the board's one lock, which is why the
/// members that make one are internal: they assume the caller holds it (RUNS-002).
/// </para>
/// </remarks>
public abstract class Queued
{
    /// <summary>
    /// The board's lock, shared with everything on it. One lock rather than one per object: the
    /// single-run rule is decided by reading all of them together, so a state that could change
    /// while that read is under way would let two runs start at once. The harness reports from
    /// whatever thread its adapter reads on, which is why this is not theoretical (RUNS-002).
    /// </summary>
    private protected readonly Lock Gate;

    private Guid? runId;
    private RunOutcome? terminal;
    private RunEndedBecause? because;
    private DateTimeOffset? acknowledgedAt;
    private RunFigures? figures;

    private protected Queued(Guid id, Lock gate)
    {
        Id = id;
        Gate = gate;
    }

    public Guid Id { get; }

    /// <summary>
    /// The run this was given, or null while it waits its turn. Not a state of its own, but what
    /// tells one waiting its turn apart from one whose agent has not yet reported in.
    /// </summary>
    public Guid? RunId
    {
        get
        {
            lock (Gate)
            {
                return runId;
            }
        }
    }

    /// <summary>
    /// Its run's figures, or null while it has no run — and not zeros, which would claim a run that
    /// spent nothing rather than no run at all (RUNS-010, ACCESS-005, ACCESS-008).
    /// </summary>
    public RunFigures? Figures
    {
        get
        {
            lock (Gate)
            {
                return figures;
            }
        }
    }

    /// <summary>
    /// Waiting its turn: accepted, never handed out. Assumes the board's lock.
    /// </summary>
    internal bool IsWaiting => runId is null && terminal is null;

    /// <summary>
    /// Under way: the board has handed it out and its run has not ended. The one condition that
    /// stops another run starting (RUNS-002). Assumes the board's lock.
    /// </summary>
    internal bool IsUnderWay => runId is not null && terminal is null;

    /// <summary>
    /// Blocking: a failure nobody has acknowledged — the one thing that holds the queue (RUNS-003).
    /// It holds a question exactly as it holds a submission. Assumes the board's lock.
    /// </summary>
    internal bool IsUnacknowledgedFailure => terminal == RunOutcome.Failed && acknowledgedAt is null;

    /// <summary>How its run ended, or null while it has not. Assumes the board's lock.</summary>
    private protected RunOutcome? Terminal => terminal;

    /// <summary>
    /// Why its run ended, where that run failed — one of <see cref="RunEndedBecause"/>'s reasons.
    /// Assumes the board's lock.
    /// </summary>
    private protected RunEndedBecause? EndedBecause => because;

    /// <summary>Whether a failure is still waiting to be acknowledged. Assumes the board's lock.</summary>
    private protected bool FailureIsWaitingToBeSeen => IsUnacknowledgedFailure;

    /// <summary>
    /// The board has handed this out to a run. Assumes the board's lock, which is what makes handing
    /// the same thing out twice impossible rather than unlikely.
    /// </summary>
    internal virtual void HandedTo(Guid run, string model)
    {
        if (runId is not null)
        {
            throw new InvalidOperationException($"{Id} already has run {runId}");
        }

        runId = run;

        // The model is known the moment the run exists, and the three figures start at nothing. From
        // here on there is a run, which is what the browser reads the fields off (ACCESS-005,
        // ACCESS-008).
        figures = new RunFigures(model, CostSpent: 0, Tokens: default, ToolCalls: 0, EntriesLost: 0);
    }

    /// <summary>
    /// Its run ended, done or failed, and — where it failed — why (RUNS-008, QUERY-006). Terminal,
    /// and there is no way out: acknowledging a failure is not one. Assumes the board's lock.
    /// </summary>
    internal void Ended(RunOutcome outcome, RunEndedBecause? reason = null)
    {
        // A run ends done or failed, and nothing else reaches this. Refused rather than read around:
        // `StateNow` treats a value it does not know as not yet terminal while the board would write it
        // down as failed, so the board and the store would disagree and the queue would stay blocked on
        // something that reads as still running. What cannot be read is refused, as a `tools` array
        // that is not names already is (GUARD-001's precedent).
        if (outcome is not (RunOutcome.Done or RunOutcome.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "a run ends done or failed");
        }

        // Guard and write under the one lock: read separately, a report and an ending racing would
        // both pass, and the later write could put a terminal one back.
        if (terminal is { } already)
        {
            throw new InvalidOperationException($"{already} is terminal; {Id} does not leave it");
        }

        terminal = outcome;
        because = outcome == RunOutcome.Failed ? reason : null;
    }

    /// <summary>
    /// The user has seen that its run failed. Not a state, and no state changes: the acknowledged run
    /// still reads failed (RUNS-003). Assumes the board's lock.
    /// </summary>
    internal void Acknowledged(DateTimeOffset at) => acknowledgedAt = at;

    /// <summary>
    /// Its run's figures as they now stand. Returns whether any of them actually changed, so that the
    /// board writes the store only where one has risen (RUNS-010, research.md R-06). Assumes the
    /// board's lock.
    /// </summary>
    internal bool FiguresAre(long costSpent, ModelTokens tokens, int toolCalls, int entriesLost)
    {
        if (figures is not { } held)
        {
            // No run, so there are no figures to be. A report for a run this does not have is a report
            // about something else.
            return false;
        }

        var risen = held with
        {
            CostSpent = costSpent,
            Tokens = tokens,
            ToolCalls = toolCalls,
            EntriesLost = entriesLost,
        };

        if (risen == held)
        {
            return false;
        }

        figures = risen;
        return true;
    }

    /// <summary>
    /// What the store held, put back as it was. Assumes the board's lock.
    /// </summary>
    private protected void RestoredTo(
        Guid? run, RunOutcome? outcome, DateTimeOffset? acknowledged, RunFigures? held)
    {
        runId = run;
        terminal = outcome;
        acknowledgedAt = acknowledged;
        figures = held;
    }
}

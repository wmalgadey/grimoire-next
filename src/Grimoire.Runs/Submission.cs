using System.Text.RegularExpressions;
using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// The four states a submission can be in, and no more (RUNS-001). <c>Done</c> and <c>Failed</c>
/// are terminal; there is no transition out of either, and acknowledging a failure is not one —
/// an acknowledged run still reads failed (RUNS-003).
/// </summary>
public enum SubmissionState
{
    Submitted,
    Running,
    Done,
    Failed,
}

/// <summary>
/// A run's figures as the list shows them: which model it runs on, what it has spent, how many calls
/// it has made, and how many entries its record could not hold (ACCESS-005, RUNS-010, RUNS-007).
/// </summary>
/// <param name="CostSpent">
/// What the run has cost, in input-token equivalents: the same quantity the cost ceiling counts,
/// over every model the run touched — not a second definition of cost, and never currency
/// (GUARD-004, DEC-015).
/// </param>
/// <param name="Tokens">
/// The four raw counts behind <see cref="CostSpent"/>. They survive a stop with it, because the
/// weighting cannot be undone and the ceiling is calibrated from them (GUARD-004, RUNS-010).
/// </param>
public sealed record RunFigures(string Model, long CostSpent, ModelTokens Tokens, int ToolCalls, int EntriesLost);

/// <summary>
/// What a submission reads at one instant: its state, whether its failure is still waiting to be
/// acknowledged, and — where it has a run — that run's figures.
/// </summary>
/// <remarks>
/// They travel together because they have to be <em>read</em> together. Asked for one after the
/// other, a submission ending between the two answers would report <c>running</c> beside an
/// acknowledgement that is available — a pair the browser's contract says cannot occur, and one
/// that would put a control on a row whose run is still under way — or <c>running</c> beside a final
/// figure, a pair that never existed (contracts/hub-http-api.md, ACCESS-003, ACCESS-005).
/// <para>
/// <see cref="Run"/> is null for a submission that has no run — one waiting its turn (RUNS-002). That
/// is what makes ACCESS-005's "for a submission that has a run" a property of the response rather than
/// a rule the browser applies: there is nothing true to say about a run that does not exist, and zeros
/// would claim a run that spent nothing.
/// </para>
/// </remarks>
public sealed record SubmissionStatus(SubmissionState State, bool AwaitingAcknowledgement, RunFigures? Run);

/// <summary>
/// One submission as the browser is told it, read at one instant: what tells it from another, and
/// its state and figures (ACCESS-004, ACCESS-005).
/// </summary>
/// <remarks>
/// A value rather than the submission itself, because the whole list has to be one instant and not a
/// row at a time: <see cref="RunBoard.Snapshot"/> builds all of these in one pass of the one
/// lock, and nothing can change between two of them. Handed the submissions instead, a caller reads
/// each one's <see cref="Submission.Status"/> separately and a run ending between two rows would make
/// a list that combines two instants.
/// </remarks>
public sealed record SubmissionSnapshot(
    Guid Id,
    string Excerpt,
    DateTimeOffset SubmittedAt,
    SubmissionStatus Status)
{
    /// <summary>
    /// Assumes the board's lock. <see cref="Submission.Status"/> takes it again, which on the same
    /// thread succeeds and holds every other caller out — so the reading below is inside the one pass
    /// rather than beside it.
    /// </summary>
    internal static SubmissionSnapshot Of(Submission submission) =>
        new(submission.Id, submission.Excerpt, submission.SubmittedAt, submission.Status);
}

/// <summary>
/// A text the user handed to Grimoire and that was <em>accepted</em>, together with its state and
/// the run it has been given. A refused text never becomes one: nothing about it is stored and it
/// carries no state (INGEST-003, INGEST-004).
/// </summary>
/// <remarks>
/// Every state change is made through <see cref="RunBoard"/> and under the board's lock,
/// because the queue rule is decided by reading all of them together (RUNS-002). The members that
/// make a change here are internal for that reason: they assume the caller holds that lock.
/// </remarks>
public sealed partial class Submission : Queued
{
    /// <summary>
    /// How much of a submitted text the browser is given. The same length for every submission is
    /// what ACCESS-004 asks; 120 characters is about a line and a half of the page's 42rem column,
    /// which is long enough to recognise one's own text and short enough that the list stays one
    /// row per submission (research.md R-07).
    /// </summary>
    private const int ExcerptLength = 120;

    /// <summary>
    /// Whether the agent of its run has reported in — the <c>system/init</c> of
    /// <c>contracts/agent-cli-protocol.md</c>. The one thing about a submission's state that
    /// <see cref="Queued"/> cannot answer from the run alone: <c>submitted</c> and <c>running</c>
    /// are both "has a run that has not ended", and this is what tells them apart (RUNS-001).
    /// </summary>
    private bool reportedIn;

    internal Submission(Guid id, string text, DateTimeOffset submittedAt, Lock gate)
        : base(id, gate)
    {
        Text = text;
        SubmittedAt = submittedAt;
    }

    /// <summary>
    /// The text as the user gave it. INGEST-004 refuses one that is empty after trimming, but what
    /// survives that is kept whole: the agent receives what the user pasted, not a tidied version
    /// of it.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// When the submission was made, and shown in the browser (ACCESS-004). <b>Not</b> what the
    /// queue is ordered by: this clock is not monotonic, so a correction can leave a later
    /// submission with an earlier stamp. The order is the order they were accepted — the board's
    /// own list, and `rowid` in the store (RUNS-002, <see cref="RunBoard.TakeNext"/>).
    /// </summary>
    public DateTimeOffset SubmittedAt { get; }

    /// <summary>
    /// The opening of <see cref="Text"/>, so that the user can tell one submission from another —
    /// and, before acknowledging a failure, tell which text the failed run was working on
    /// (ACCESS-004). Derived and never stored: it is a reading of the text, not a second copy of it.
    /// </summary>
    public string Excerpt => ExcerptOf(Text);

    /// <summary>
    /// Exactly one state at a time (RUNS-001), and <b>read</b> rather than stored: done and failed
    /// are the run's ending, running is its agent having reported in, and submitted is everything
    /// before that. Stored beside those facts it could disagree with the queue rule, which reads
    /// them (<see cref="Queued"/>).
    /// </summary>
    public SubmissionState State
    {
        get
        {
            lock (Gate)
            {
                return StateNow;
            }
        }
    }

    /// <summary>
    /// This submission's run failed and the user has not acknowledged it yet — the one thing that
    /// holds the queue (RUNS-003), and the one row in the browser that offers the control
    /// (ACCESS-003). It says an action is available, not what the run did.
    /// </summary>
    public bool AwaitingAcknowledgement => Status.AwaitingAcknowledgement;

    /// <summary>
    /// The state and the acknowledgement as of one instant, read under the one lock. What the
    /// browser is told is built from this rather than from the two properties in turn, so that the
    /// pair it renders is a pair that actually existed (<see cref="SubmissionStatus"/>).
    /// </summary>
    public SubmissionStatus Status
    {
        get
        {
            lock (Gate)
            {
                return new SubmissionStatus(StateNow, FailureIsWaitingToBeSeen, Figures);
            }
        }
    }

    /// <summary>Assumes the board's lock.</summary>
    private SubmissionState StateNow => Terminal switch
    {
        RunOutcome.Done => SubmissionState.Done,
        RunOutcome.Failed => SubmissionState.Failed,
        _ => reportedIn ? SubmissionState.Running : SubmissionState.Submitted,
    };

    /// <summary>
    /// This one submission as the browser is told it, at one instant.
    /// </summary>
    /// <remarks>
    /// For the one answer that is about a single submission — the accepted one, answered to whoever
    /// submitted it. A list is read through <see cref="RunBoard.Snapshot"/> instead, because
    /// there the instant has to span every row.
    /// </remarks>
    public SubmissionSnapshot Snapshot => SubmissionSnapshot.Of(this);

    /// <summary>
    /// Whitespace collapsed, trimmed, and cut to <see cref="ExcerptLength"/> with an ellipsis where
    /// it was cut. Collapsing is what makes the first line of a pasted document read as a sentence
    /// rather than as an indented fragment; a text at or under the length is returned whole, with
    /// nothing appended and nothing padded (research.md R-07).
    /// </summary>
    private static string ExcerptOf(string text)
    {
        var collapsed = Whitespace().Replace(text, " ").Trim();

        if (collapsed.Length <= ExcerptLength)
        {
            return collapsed;
        }

        // A character outside the basic plane is two chars wide. Half of one is not the opening of
        // anything, and the browser would render it as a replacement glyph, so the cut moves back.
        var cut = char.IsHighSurrogate(collapsed[ExcerptLength - 1]) ? ExcerptLength - 1 : ExcerptLength;

        return string.Concat(collapsed.AsSpan(0, cut), "…");
    }

    /// <summary>Every run of whitespace, newlines and tabs among it, as one thing to replace.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// What the store held, put back as it was. The rule that turns it into a state — a run that
    /// was in progress reads failed — is the board's, and is applied after this (RUNS-004).
    /// Assumes the board's lock.
    /// </summary>
    internal void Restored(StoredSubmission held)
    {
        ArgumentNullException.ThrowIfNull(held);

        // The figures come back with the run, which is what makes a run cut off by a stop read failed
        // and still carry what it spent (RUNS-010, RUNS-004).
        RestoredTo(
            held.Run?.Id,
            held.State switch
            {
                SubmissionState.Done => RunOutcome.Done,
                SubmissionState.Failed => RunOutcome.Failed,
                _ => null,
            },
            held.AcknowledgedAt,
            held.Run is { } run
                ? new RunFigures(run.Model, run.CostSpent, run.Tokens, run.ToolCalls, run.EntriesLost)
                : null);

        // A submission the store read as `running` had reported in before the stop, and one that reads
        // done or failed had too — a run does not end before its agent starts. Read back rather than
        // stored, so nothing new goes on disk for it (RUNS-004).
        reportedIn = held.State is not SubmissionState.Submitted;
    }

    /// <summary>
    /// The agent has reported in — the <c>system/init</c> event of
    /// <c>contracts/agent-cli-protocol.md</c>. This is the boundary the spec draws: a submission
    /// reads <c>Submitted</c> from acceptance until here and <c>Running</c> from here on. Assumes
    /// the board's lock.
    /// </summary>
    internal void ReportedIn()
    {
        if (StateNow != SubmissionState.Submitted)
        {
            throw new InvalidOperationException($"a submission reading {StateNow} cannot start running");
        }

        reportedIn = true;
    }
}

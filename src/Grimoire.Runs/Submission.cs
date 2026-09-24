using System.Text.RegularExpressions;

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
/// What a submission reads at one instant: its state, and whether its failure is still waiting to
/// be acknowledged.
/// </summary>
/// <remarks>
/// The two travel together because they have to be <em>read</em> together. Asked for one after the
/// other, a submission ending between the two answers would report <c>running</c> beside an
/// acknowledgement that is available — a pair the browser's contract says cannot occur, and one
/// that would put a control on a row whose run is still under way
/// (contracts/hub-http-api.md, ACCESS-003).
/// </remarks>
public sealed record SubmissionStatus(SubmissionState State, bool AwaitingAcknowledgement);

/// <summary>
/// A text the user handed to Grimoire and that was <em>accepted</em>, together with its state and
/// the run it has been given. A refused text never becomes one: nothing about it is stored and it
/// carries no state (INGEST-003, INGEST-004).
/// </summary>
/// <remarks>
/// Every state change is made through <see cref="SubmissionBoard"/> and under the board's lock,
/// because the queue rule is decided by reading all of them together (RUNS-002). The members that
/// make a change here are internal for that reason: they assume the caller holds that lock.
/// </remarks>
public sealed partial class Submission
{
    /// <summary>
    /// How much of a submitted text the browser is given. The same length for every submission is
    /// what ACCESS-004 asks; 120 characters is about a line and a half of the page's 42rem column,
    /// which is long enough to recognise one's own text and short enough that the list stays one
    /// row per submission (research.md R-07).
    /// </summary>
    private const int ExcerptLength = 120;

    /// <summary>
    /// The board's lock, shared with every submission on it. One lock rather than one per object:
    /// the single-run rule is decided by reading the states of all of them together
    /// (RUNS-002), so a state that could change while that read is under way would let two runs
    /// start at once. The harness reports from whatever thread its adapter reads on, which is why
    /// this is not theoretical.
    /// </summary>
    private readonly Lock gate;

    private SubmissionState state = SubmissionState.Submitted;
    private Guid? runId;
    private DateTimeOffset? acknowledgedAt;

    internal Submission(Guid id, string text, DateTimeOffset submittedAt, Lock gate)
    {
        Id = id;
        Text = text;
        SubmittedAt = submittedAt;
        this.gate = gate;
    }

    public Guid Id { get; }

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
    /// own list, and `rowid` in the store (RUNS-002, <see cref="SubmissionBoard.TakeNext"/>).
    /// </summary>
    public DateTimeOffset SubmittedAt { get; }

    /// <summary>
    /// The opening of <see cref="Text"/>, so that the user can tell one submission from another —
    /// and, before acknowledging a failure, tell which text the failed run was working on
    /// (ACCESS-004). Derived and never stored: it is a reading of the text, not a second copy of it.
    /// </summary>
    public string Excerpt => ExcerptOf(Text);

    /// <summary>Exactly one state at a time (RUNS-001).</summary>
    public SubmissionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <summary>
    /// The run this submission was given, or null while it waits its turn. Not a state — RUNS-001's
    /// four stay four — but what tells a submission waiting its turn apart from one whose agent has
    /// not yet reported in, since both read <c>submitted</c> (research.md R-04). It does not reach
    /// the browser (ACCESS-002).
    /// </summary>
    public Guid? RunId
    {
        get
        {
            lock (gate)
            {
                return runId;
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
            lock (gate)
            {
                return new SubmissionStatus(
                    state,
                    state == SubmissionState.Failed && acknowledgedAt is null);
            }
        }
    }

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
    /// Blocking: a failure nobody has acknowledged. Assumes the board's lock.
    /// </summary>
    internal bool IsUnacknowledgedFailure => state == SubmissionState.Failed && acknowledgedAt is null;

    /// <summary>
    /// The user has seen that this submission's run failed. Not a state, and no state changes: the
    /// acknowledged run still reads failed (RUNS-003). Assumes the board's lock.
    /// </summary>
    internal void Acknowledged(DateTimeOffset at) => acknowledgedAt = at;

    /// <summary>
    /// Under way: the board has handed this submission out and its run has not ended. The one
    /// condition that stops another run starting (RUNS-002). Assumes the board's lock.
    /// </summary>
    internal bool IsUnderWay => runId is not null && state is not (SubmissionState.Done or SubmissionState.Failed);

    /// <summary>
    /// Waiting its turn: accepted, never handed out. Assumes the board's lock.
    /// </summary>
    internal bool IsWaiting => runId is null && state == SubmissionState.Submitted;

    /// <summary>
    /// What the store held, put back as it was. The rule that turns it into a state — a run that
    /// was in progress reads failed — is the board's, and is applied after this (RUNS-004).
    /// Assumes the board's lock.
    /// </summary>
    internal void Restored(StoredSubmission held)
    {
        state = held.State;
        runId = held.Run?.Id;
        acknowledgedAt = held.AcknowledgedAt;
    }

    /// <summary>
    /// The board has handed this submission out to a run. Assumes the board's lock, which is what
    /// makes handing the same submission out twice impossible rather than unlikely.
    /// </summary>
    internal void HandedTo(Guid run)
    {
        if (runId is not null)
        {
            throw new InvalidOperationException($"submission {Id} already has run {runId}");
        }

        runId = run;
    }

    /// <summary>
    /// The agent has reported in — the <c>system/init</c> event of
    /// <c>contracts/agent-cli-protocol.md</c>. This is the boundary the spec draws: a submission
    /// reads <c>Submitted</c> from acceptance until here and <c>Running</c> from here on. Assumes
    /// the board's lock.
    /// </summary>
    internal void ReportedIn()
    {
        if (state != SubmissionState.Submitted)
        {
            throw new InvalidOperationException($"a submission reading {state} cannot start running");
        }

        state = SubmissionState.Running;
    }

    /// <summary>
    /// The run ended. A run can end before the agent ever reports in — that is where the grant is
    /// checked, and a surface that is not the grant ends the run failed there (GUARD-001). Assumes
    /// the board's lock.
    /// </summary>
    internal void Ended(SubmissionState terminal)
    {
        if (terminal is not (SubmissionState.Done or SubmissionState.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(terminal), terminal, "a run ends done or failed");
        }

        // Guard and write under the one lock: read separately, a report-in and an end racing on a
        // submission still reading Submitted would both pass, and the later write could put a
        // terminal submission back to Running.
        if (state is SubmissionState.Done or SubmissionState.Failed)
        {
            throw new InvalidOperationException($"{state} is terminal; a submission does not leave it");
        }

        state = terminal;
    }
}

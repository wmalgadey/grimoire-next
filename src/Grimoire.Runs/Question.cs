using Grimoire.Agent;

namespace Grimoire.Runs;

/// <summary>
/// The four values a question reads in the chat, and no more (ACCESS-007). Four values inside one
/// requirement, never one requirement per value (Constitution IV.7).
/// </summary>
public enum QuestionState
{
    /// <summary>Accepted, and something else is running or a failure is holding the queue (RUNS-002).</summary>
    Waiting,

    /// <summary>Its run is under way, and the answer is forming.</summary>
    Answering,

    /// <summary>Its run ended done.</summary>
    Answered,

    /// <summary>Its run ended failed, and <see cref="Question.Because"/> says why (QUERY-006).</summary>
    NoAnswer,
}

/// <summary>
/// What a question reads at one instant: which of the four it is, why it got no answer where that is
/// what it is, whether that failure is still waiting to be acknowledged, and its run's figures.
/// </summary>
/// <remarks>
/// They travel together for the reason <see cref="SubmissionStatus"/>'s do: asked one after another,
/// a run ending between two answers would report <em>being answered</em> beside an acknowledgement
/// that is available, or beside a final figure — pairs that never existed (ACCESS-007, ACCESS-008).
/// </remarks>
public sealed record QuestionStatus(
    QuestionState State,
    RunEndedBecause? Because,
    bool AwaitingAcknowledgement,
    RunFigures? Run);

/// <summary>
/// A text the user asked the wiki, and that was <em>accepted</em> (QUERY-001).
/// </summary>
/// <remarks>
/// <para>
/// Created only when a question is accepted. A refused one becomes nothing: it is stored nowhere and
/// carries no state (QUERY-003).
/// </para>
/// <para>
/// <b>Not persisted.</b> Nothing of a question reaches disk — not its text, not its answer, not its
/// steps (QUERY-005). Its <em>run</em> is persisted, because RUNS-006 has a start-up terminate the
/// agent of every run that was in progress and a run with nothing recorded would leave an orphaned
/// <c>claude</c> holding the granted tools with no ceiling on it.
/// </para>
/// <para>
/// It carries <b>no state field</b>. The four values above are read from whether there is a run and
/// whether it has ended, which is the same pair of facts the queue rule reads — so the chat and the
/// queue can never disagree about a question (<see cref="Queued"/>, research.md R-03).
/// </para>
/// </remarks>
public sealed class Question : Queued
{
    /// <summary>
    /// Whether the chat that held this question has been put away. Assumes the board's lock.
    /// </summary>
    private bool theChatIsGone;

    internal Question(Guid id, string text, DateTimeOffset askedAt, Lock gate)
        : base(id, gate)
    {
        Text = text;
        AskedAt = askedAt;
    }

    /// <summary>
    /// A failure nobody can see does not hold the queue (RUNS-003).
    /// </summary>
    /// <remarks>
    /// <para>
    /// RUNS-003's last clause, reaching the case a new chat makes: the question is on no screen and
    /// the control that would clear it went with the turn it sat on, so a block here is one **nothing
    /// could ever lift** — a Grimoire that does not run again until it is restarted. And starting a
    /// new chat is the remedy this feature offers for a question that failed, so the block would be
    /// reached by the very act meant to escape it.
    /// </para>
    /// <para>
    /// It is asked at the moment the queue reads it rather than settled when the chat went, because a
    /// question that was still <em>being answered</em> then has not failed yet: its run goes on being
    /// a run and goes on holding the queue while it does (QUERY-005), and only when it ends failed is
    /// there a failure to disregard.
    /// </para>
    /// </remarks>
    internal override bool IsUnacknowledgedFailure => base.IsUnacknowledgedFailure && !theChatIsGone;

    /// <summary>
    /// The chat that held this question has been put away (QUERY-005). Assumes the board's lock.
    /// </summary>
    internal void TheChatIsGone() => theChatIsGone = true;

    /// <summary>
    /// What the user asked, whole, as they typed it. QUERY-003 refuses one that is empty after
    /// trimming, but what survives that is kept as it was given: the agent receives what the user
    /// wrote, not a tidied version of it (QUERY-001, QUERY-002).
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// When it was asked, and shown in the chat. <b>Not</b> what the queue is ordered by: this clock
    /// is not monotonic, so a correction can leave a later question with an earlier stamp. The order
    /// is the order things were accepted — the board's own list (RUNS-002, <see cref="RunBoard"/>).
    /// </summary>
    public DateTimeOffset AskedAt { get; }

    /// <summary>Exactly one of the four, read rather than stored (ACCESS-007).</summary>
    public QuestionState State
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
    /// Why it got no answer, and null in every other case. One of <see cref="RunEndedBecause"/>'s
    /// reasons, which is what the chat says beside the question (QUERY-006).
    /// </summary>
    public RunEndedBecause? Because
    {
        get
        {
            lock (Gate)
            {
                return StateNow == QuestionState.NoAnswer ? EndedBecause : null;
            }
        }
    }

    /// <summary>
    /// All of it as of one instant, read under the one lock. What the chat is told is built from this
    /// rather than from the properties in turn, so that the pair it draws is a pair that existed.
    /// </summary>
    public QuestionStatus Status
    {
        get
        {
            lock (Gate)
            {
                return new QuestionStatus(
                    StateNow,
                    StateNow == QuestionState.NoAnswer ? EndedBecause : null,
                    FailureIsWaitingToBeSeen,
                    Figures);
            }
        }
    }

    /// <summary>Assumes the board's lock.</summary>
    private QuestionState StateNow => Terminal switch
    {
        RunOutcome.Done => QuestionState.Answered,
        RunOutcome.Failed => QuestionState.NoAnswer,

        // A run that has not ended is one being answered; no run at all is one waiting its turn.
        // There is no report-in step here, unlike a submission: what the chat shows is that the answer
        // is forming, and it forms from the agent's first word rather than from `system/init`.
        _ => RunId is null ? QuestionState.Waiting : QuestionState.Answering,
    };
}

using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// One thing the agent did to reach an answer: a tool call, or what one returned (ACCESS-007).
/// </summary>
/// <param name="Kind">
/// <c>called</c> or <c>returned</c>. The two kinds of <see cref="RunMomentKind"/> a chat shows; the
/// agent's own text is the answer, and <c>GrimoireSaid</c> never arises for a question's run because
/// it carries only RUNS-005's nudge, which is asked of a run that is to change the wiki.
/// </param>
/// <param name="Content">
/// What went in, or what came back — <b>whole</b>, never cut and never summarised. The user checking
/// an answer is checking exactly this (ACCESS-007, RUNS-009's precedent).
/// </param>
public sealed record ChatStep(string Kind, string? Tool, string? Content)
{
    public const string Called = "called";
    public const string Returned = "returned";
}

/// <summary>
/// Which of the four things changed about the chat, and where (ACCESS-007).
/// </summary>
/// <param name="Question">Whose turn it is about.</param>
/// <param name="From">For <see cref="Answer"/>: how far into that turn's answer this piece starts.</param>
/// <param name="To">For <see cref="Answer"/>: where it ends.</param>
/// <param name="Step">For <see cref="Step"/>: which of that turn's steps it is.</param>
/// <remarks>
/// <para>
/// A <b>descriptor</b> and not a copy: the answer's text and the step's content live in the turn, and
/// this says which slice of them the change was. So the list of changes a stream reads forward is a
/// handful of integers per event however large the chat is, and a piece of an answer cannot be held
/// here saying one thing while the turn says another.
/// </para>
/// <para>
/// Safe because a turn only ever grows: an answer is appended to and a step is added after the last,
/// so the slice this names means the same thing whenever it is read.
/// </para>
/// </remarks>
public sealed record ChatChange(string Event, Guid Question, int From = 0, int To = 0, int Step = 0);

/// <summary>
/// The four increments a chat's stream carries, by the names the browser listens for
/// (contracts/hub-http-api.md).
/// </summary>
/// <remarks>
/// Their own class rather than constants on <see cref="ChatChange"/>, because three of the four are
/// also the names of that record's own members and a type cannot hold both.
/// </remarks>
public static class ChatEvents
{
    /// <summary>The snapshot every stream opens with: the whole chat.</summary>
    public const string Chat = "chat";

    /// <summary>A question joined the chat.</summary>
    public const string Asked = "asked";

    /// <summary>The agent wrote more of an answer.</summary>
    public const string Answer = "answer";

    /// <summary>The agent made a call, or one returned.</summary>
    public const string Step = "step";

    /// <summary>A question's state or figures changed.</summary>
    public const string Question = "question";
}

/// <summary>
/// One question in the chat, with the answer forming under it and the steps the agent took
/// (QUERY-005, ACCESS-007).
/// </summary>
/// <remarks>
/// <para>
/// It holds the <see cref="Question"/> rather than a copy of its state, so the four values the chat
/// shows and the queue rule that reads the same facts can never disagree (research.md R-03).
/// </para>
/// <para>
/// <b>Where the line falls between the answer and the steps</b>: every piece of the agent's own text
/// is the answer, appended in the order it arrived; the tool calls and their results are the steps.
/// It is the only split that can be decided at the moment a block arrives and never revised, which is
/// what ACCESS-007's "content arriving must not move what the user is already reading" requires
/// (research.md R-08).
/// </para>
/// </remarks>
public sealed class ChatTurn(Question question)
{
    private readonly List<ChatStep> steps = [];
    private readonly Lock gate = new();
    private string answer = string.Empty;

    public Question Question { get; } = question;

    /// <summary>
    /// The agent's own text so far, in the order it arrived, as one piece of prose — possibly empty,
    /// for a question waiting its turn or one whose run has said nothing yet.
    /// </summary>
    public string Answer
    {
        get
        {
            lock (gate)
            {
                return answer;
            }
        }
    }

    /// <summary>One per tool call and one per result, in the order they happened.</summary>
    public IReadOnlyList<ChatStep> Steps
    {
        get
        {
            lock (gate)
            {
                return [.. steps];
            }
        }
    }

    /// <summary>
    /// What this question's run has spent, or null while it has no run — and not a zero, because
    /// there is nothing true to say about a run that does not exist (ACCESS-008, RUNS-010).
    /// </summary>
    public long? CostSpent => Question.Figures?.CostSpent;

    internal void AgentSaid(string text)
    {
        lock (gate)
        {
            answer += text;
        }
    }

    internal void StepHappened(ChatStep step)
    {
        lock (gate)
        {
            steps.Add(step);
        }
    }
}

/// <summary>
/// One turn as it stood at one instant: the question, and <b>copies</b> of the answer and the steps
/// as they were (ACCESS-007).
/// </summary>
/// <remarks>
/// The two growing things are copied because a snapshot has to agree with the position in the change
/// log that was taken with it. The <see cref="Question"/> is the live object, because its state and
/// figures are read through the board and are idempotent to re-send.
/// </remarks>
public sealed record ChatTurnAsItWas(Question Question, string Answer, IReadOnlyList<ChatStep> Steps);

/// <summary>
/// The chat at one instant, with the position in its change log that goes with it (ACCESS-007).
/// </summary>
/// <param name="Changes">
/// The whole change log as it stood, so that a reader of it and the turns it refers to are the same
/// instant. Its <c>Count</c> is the position a subscriber is left at.
/// </param>
public sealed record ChatSnapshot(
    IReadOnlyList<ChatTurnAsItWas> Turns,
    IReadOnlyList<ChatChange> Changes,
    int Generation);

/// <summary>
/// The current conversation: the questions asked in it, their answers, and what the agent did under
/// each (QUERY-005).
/// </summary>
/// <remarks>
/// <para>
/// <b>A plain class and not a port.</b> Nothing outside the process is behind it and no second
/// implementation exists, which is when Constitution II.4 forbids an interface — and a persistent
/// second implementation would contradict QUERY-005 rather than serve it, because "gone after a
/// restart" is the requirement and not a limitation (research.md R-02).
/// </para>
/// <para>
/// <b>Exactly one, and nothing of it on disk.</b> QUERY-005 says so, and one object every stream
/// reads is what makes "starting a new chat empties both tabs" fall out rather than being built.
/// Held while nobody is connected, too: the chat is the hub's and not a subscriber's, so a run whose
/// reader walked away still writes into it and a browser that comes back reads it.
/// </para>
/// <para>
/// <b>It is not a record and is never written down.</b> A record exists because a run is handed over
/// and reviewed afterwards; a chat is read as it happens, so a file for it would be one nobody opens
/// (Constitution II.1, RUNS-007).
/// </para>
/// </remarks>
public sealed class Chat(Chat.Changed? changed = null)
{
    /// <summary>
    /// Something in the chat changed: every browser reading it is woken (ACCESS-007).
    /// </summary>
    /// <remarks>
    /// A delegate the composition root supplies, which is the precedent <c>RunBoard.Changed</c> sets.
    /// Raised at the end of <b>every</b> method here that writes something, which is what makes
    /// "a change is always followed by a wake" a property of this class rather than a thing each
    /// caller has to remember — a caller that forgot left an accepted question undrawn until something
    /// else happened to change.
    /// <para>
    /// Raised inside the chat's lock, and the lock order is <b>board → chat → live</b> throughout. What
    /// it does must therefore not take the board: the hub's <c>LiveUpdates.Changed</c> writes a byte to
    /// a channel per subscriber and does not.
    /// </para>
    /// </remarks>
    public delegate void Changed();

    private readonly Lock gate = new();
    private readonly List<ChatChange> changes = [];

    /// <summary>
    /// Which turn a run is answering, kept here rather than asked of the board.
    /// </summary>
    /// <remarks>
    /// <b>This is what keeps the two locks in one order.</b> Finding the turn by reading
    /// <c>Question.RunId</c> takes the <em>board's</em> lock, and the board raises this chat's changes
    /// from inside that same lock — so a moment arriving while a question was accepted could leave one
    /// thread holding the chat and wanting the board while another held the board and wanted the chat.
    /// The chat is told which run belongs to which turn instead, by the caller that already holds the
    /// board, and never asks.
    /// </remarks>
    private readonly Dictionary<Guid, ChatTurn> answering = [];

    private List<ChatTurn> turns = [];
    private int generation;

    /// <summary>In order, oldest first — which is the order a conversation is read in.</summary>
    public IReadOnlyList<ChatTurn> Turns
    {
        get
        {
            lock (gate)
            {
                return turns;
            }
        }
    }

    /// <summary>
    /// What every question in this chat has spent altogether, a failed one included — <b>no ceiling
    /// stands beside it</b>, because each question carries its own and a total dressed as
    /// <c>x / y</c> would invent one that does not exist (ACCESS-008).
    /// </summary>
    /// <remarks>
    /// A sum over the turns of the runs' own figures (DEC-030, RUNS-010), so nothing is counted a
    /// second time — which is also why a failed question's spend is in it: a run that failed still
    /// spent.
    /// </remarks>
    public long Total => Turns.Sum(turn => turn.CostSpent ?? 0);

    /// <summary>
    /// Which chat this is. Rises when a new one is started, so a stream can tell that the changes it
    /// was reading forward belong to a conversation that is gone (QUERY-005).
    /// </summary>
    public int Generation
    {
        get
        {
            lock (gate)
            {
                return generation;
            }
        }
    }

    /// <summary>A question joined the chat, reading <em>waiting its turn</em> (QUERY-001).</summary>
    public void Ask(Question question)
    {
        ArgumentNullException.ThrowIfNull(question);

        lock (gate)
        {
            // A new list rather than an append, so that `Turns` can hand out what it holds without
            // copying: a reader that has the old list reads a chat that existed, and the one
            // subscribers are given next is the one with this question in it.
            turns = [.. turns, new ChatTurn(question)];
            changes.Add(new ChatChange(ChatEvents.Asked, question.Id));
            changed?.Invoke();
        }
    }

    /// <summary>A piece of the answer, appended as it arrives (ACCESS-007).</summary>
    public void AgentSaid(Guid runId, string text)
    {
        lock (gate)
        {
            var turn = Answering(runId);

            if (turn is null)
            {
                return;
            }

            var from = turn.Answer.Length;
            turn.AgentSaid(text);

            changes.Add(new ChatChange(ChatEvents.Answer, turn.Question.Id, from, turn.Answer.Length));
            changed?.Invoke();
        }
    }

    /// <summary>A tool call, or what one returned (ACCESS-007).</summary>
    public void StepHappened(Guid runId, ChatStep step)
    {
        lock (gate)
        {
            var turn = Answering(runId);

            if (turn is null)
            {
                return;
            }

            turn.StepHappened(step);
            changes.Add(new ChatChange(ChatEvents.Step, turn.Question.Id, Step: turn.Steps.Count - 1));
            changed?.Invoke();
        }
    }

    /// <summary>
    /// A question's state or its figures changed — it was handed a run, its run spent, or its run ended
    /// (ACCESS-007, ACCESS-008).
    /// </summary>
    /// <remarks>
    /// Said by whoever knows, which is the board through the composition root: the state and the
    /// figures are read off the question and its run rather than kept here, so this carries no values —
    /// only that there is something new to read.
    /// </remarks>
    public void QuestionChanged(Guid questionId, Guid? runId)
    {
        lock (gate)
        {
            var turn = turns.Find(turn => turn.Question.Id == questionId);

            if (turn is null)
            {
                return;
            }

            // Which run answers this turn, told rather than asked: the caller is the board, inside its
            // own lock, where the run is already known. Asking would take that lock from inside this
            // one and put the two in an order the board itself contradicts.
            if (runId is { } run)
            {
                answering[run] = turn;
            }

            changes.Add(new ChatChange(ChatEvents.Question, questionId));
            changed?.Invoke();
        }
    }

    /// <summary>
    /// Everything a stream needs, read at <b>one</b> instant: the turns, and the log of what has
    /// changed in the order it changed (ACCESS-007).
    /// </summary>
    /// <remarks>
    /// <b>The change log is not a replay buffer.</b> Nothing in it is ever sent to a subscriber that was
    /// not already reading forward from it: a browser that connects — or reconnects — is given the
    /// snapshot and starts at the <em>end</em> of the log, which answers ACCESS-007's reconnect clause
    /// with nothing replayed. What it is for is the opposite problem: a subscriber that <em>is</em>
    /// connected has to be told the one thing that changed, and "the one thing" is only knowable against
    /// what it has already been told. It holds descriptors rather than content, so it is a handful of
    /// integers per event, and it goes with the chat when a new one is started.
    /// </summary>
    /// <remarks>
    /// The answer and the steps are <b>copied</b> here rather than read from the turns afterwards, and
    /// the position in the change log is taken in the same breath. Read apart, a piece of an answer
    /// arriving between the two would be in the snapshot <em>and</em> past the subscriber's position —
    /// so the next wake would send it again and the browser would show it twice.
    /// <para>
    /// The change log comes with them, because a reader of the two has to read one instant: a question
    /// joining the chat between a reading of the changes and a reading of the turns would leave a
    /// descriptor pointing at a turn that is not there — skipped, and then skipped past for good, so
    /// the browser would never be told about that question at all.
    /// </para>
    /// <para>
    /// A turn's state and figures are deliberately <em>not</em> captured: reading them takes the board's
    /// lock, which this must never do while holding its own. They do not need to be — every change to
    /// one records a <c>question</c> descriptor, and that event carries absolute values, so sending it
    /// once more says the same thing rather than doubling anything.
    /// </para>
    /// </remarks>
    public ChatSnapshot Snapshot()
    {
        lock (gate)
        {
            return new ChatSnapshot(
                [.. turns.Select(turn => new ChatTurnAsItWas(turn.Question, turn.Answer, turn.Steps))],
                [.. changes],
                generation);
        }
    }

    /// <summary>
    /// A new chat: the turns and the total are gone and nothing of them is reachable (QUERY-005).
    /// </summary>
    /// <remarks>
    /// <b>A question still being answered is not stopped.</b> Its run is not a chat and goes on being
    /// a run — it holds the queue until it ends, and its figures stay with it — but what it produces
    /// belongs to the chat that is gone: the turn it wrote into is no longer in this one, so nothing
    /// of it appears in the new chat. Grimoire has no way to stop a run except a ceiling, and building
    /// one here would be a mechanism this feature does not otherwise need (research.md R-13).
    /// </remarks>
    public void Start()
    {
        lock (gate)
        {
            turns = [];
            changes.Clear();
            answering.Clear();
            generation++;
            changed?.Invoke();
        }
    }

    /// <summary>
    /// The turn this run is answering, or null where there is none — a question whose chat has been
    /// put away, or a run that is not a question's at all.
    /// </summary>
    /// <summary>
    /// The turn this run answers, or null where there is none — a question whose chat has been put
    /// away, or a run that is not a question's at all. Assumes the lock.
    /// </summary>
    /// <remarks>
    /// Read from what the chat was told, never from the board: see <see cref="answering"/>.
    /// </remarks>
    private ChatTurn? Answering(Guid runId) => answering.GetValueOrDefault(runId);
}

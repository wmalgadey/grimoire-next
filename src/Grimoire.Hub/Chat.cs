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
public sealed class Chat
{
    private readonly Lock gate = new();
    private List<ChatTurn> turns = [];

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
        }
    }

    /// <summary>A piece of the answer, appended as it arrives (ACCESS-007).</summary>
    public void AgentSaid(Guid runId, string text) => Answering(runId)?.AgentSaid(text);

    /// <summary>A tool call, or what one returned (ACCESS-007).</summary>
    public void StepHappened(Guid runId, ChatStep step) => Answering(runId)?.StepHappened(step);

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
        }
    }

    /// <summary>
    /// The turn this run is answering, or null where there is none — a question whose chat has been
    /// put away, or a run that is not a question's at all.
    /// </summary>
    private ChatTurn? Answering(Guid runId) => Turns.FirstOrDefault(turn => turn.Question.RunId == runId);
}

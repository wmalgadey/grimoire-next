using Grimoire.Hub.Api;
using Grimoire.Runs;

namespace Grimoire.Hub;

/// <summary>
/// The texts a run receives, and the only thing that puts text into the agent's prompt (Constitution
/// V.1).
/// </summary>
/// <remarks>
/// <para>
/// All three are read from the paths the hub was started with. Two of them are Grimoire's own,
/// versioned in this repository — the ingest instruction and the question instruction — and changing
/// either is an owner decision named in the PR; the purpose description is the user's, and Grimoire
/// neither creates nor changes it.
/// </para>
/// <para>
/// Nothing else reaches a prompt. The run is started with no settings loaded from the machine and in
/// a working directory Grimoire owns, so nothing in the wiki or on the host can add to what is
/// assembled here (research.md R-11).
/// </para>
/// <para>
/// It assembles <b>two</b> prompts since <c>004-ask-the-wiki</c>: one for a run that is to change the
/// wiki, and one for a run that answers a question. Two, and not one with a flag, because they are
/// assembled from different parts and one of them carries the conversation so far.
/// </para>
/// </remarks>
public sealed class InstructionLoader(
    string instructionPath,
    string questionInstructionPath,
    string purposeDescriptionPath)
{
    /// <summary>
    /// Whether each of the three is in place <em>now</em>. Checked per submission and per question
    /// rather than once at start-up, because INGEST-003 and QUERY-003 are about the state of those
    /// paths when the text is submitted or the question asked.
    /// </summary>
    /// <remarks>
    /// One reading for all three, so a submission and a question asked at the same moment are judged
    /// against the same facts. Which of the three refuses which is the board's
    /// (<see cref="RunBoard.Accept"/>, <see cref="RunBoard.Ask"/>): a submission is refused on the
    /// ingest instruction and the purpose description, a question on the question instruction and the
    /// purpose description, and in each case the instruction is looked at first so that a start with
    /// neither in place says exactly one thing.
    /// </remarks>
    public StartUpInputs Read() => new(
        InstructionPresent: File.Exists(instructionPath),
        QuestionInstructionPresent: File.Exists(questionInstructionPath),
        PurposeDescriptionPresent: File.Exists(purposeDescriptionPath));

    /// <summary>
    /// What a run that is to change the wiki is given: the instruction, the purpose description, the
    /// run's identifier and the submitted text, in that order and nothing besides (INGEST-002,
    /// <c>contracts/agent-cli-protocol.md</c>).
    /// </summary>
    public string Assemble(string text, Guid runId) =>
        Payload(File.ReadAllText(instructionPath), File.ReadAllText(purposeDescriptionPath), text, runId);

    /// <summary>
    /// What a question's run is given: the question instruction, the purpose description, the run's
    /// identifier, what has been asked and answered in this chat before it, and the question whole as
    /// the user typed it — in that order (QUERY-002, contracts/question-run.md §2).
    /// </summary>
    /// <remarks>
    /// <b>Nothing is trimmed and there is no cap.</b> The whole chat goes in. A chat too large for a
    /// dispatch ends that run failed, and the chat says that question got no answer and why
    /// (QUERY-006) — the path every failed run takes, with the remedy the feature already gives: a new
    /// chat. A cap, a window or a summary would be a mechanism with no consumer until a real chat
    /// reaches the limit (Constitution II.1, research.md R-07).
    /// </remarks>
    public string AssembleQuestion(Chat chat, string question, Guid runId)
    {
        ArgumentNullException.ThrowIfNull(chat);

        return QuestionPayload(
            File.ReadAllText(questionInstructionPath),
            File.ReadAllText(purposeDescriptionPath),
            ConversationSoFar(chat, runId),
            question,
            runId);
    }

    /// <summary>
    /// The payload's shape, apart from where its parts are read from — which is what lets the Fast
    /// suite prove INGEST-002 without a filesystem.
    /// </summary>
    public static string Payload(string instruction, string purposeDescription, string text, Guid runId) =>
        $"{instruction}\n\n{purposeDescription}\n\nRun id: {runId}\n\n{text}";

    /// <summary>
    /// A question's payload, apart from where its parts are read from (QUERY-002).
    /// </summary>
    /// <remarks>
    /// The conversation sits between the run's identifier and the question, because that is the order
    /// it is read in: what came before, and then what is being asked now. Left out entirely where
    /// there is none, so a first question's prompt carries no empty heading for a conversation that
    /// has not happened.
    /// </remarks>
    public static string QuestionPayload(
        string instruction,
        string purposeDescription,
        string conversationSoFar,
        string question,
        Guid runId) =>
        string.IsNullOrEmpty(conversationSoFar)
            ? $"{instruction}\n\n{purposeDescription}\n\nRun id: {runId}\n\n{question}"
            : $"{instruction}\n\n{purposeDescription}\n\nRun id: {runId}\n\n{conversationSoFar}\n\n{question}";

    /// <summary>
    /// What has been asked and answered in this chat before this run — each earlier question and the
    /// answer text it produced, in order (QUERY-002).
    /// </summary>
    /// <remarks>
    /// <b>The steps are not included.</b> What the agent did to reach an earlier answer is for the user
    /// to check, not context the next run needs, and a run's tool results are the largest thing in a
    /// chat by far (research.md R-07).
    /// <para>
    /// Two turns are left out, and each for its own reason. This run's own, because it is the question
    /// being asked and the payload carries that whole at the end. And one still being answered or
    /// waiting, because there is nothing settled to hand on.
    /// </para>
    /// <para>
    /// <b>A turn that got no answer is handed on as asked</b>, with why it got none where its answer
    /// would stand — in the words the chat shows it in. An agent not told that the question was
    /// already asked and failed walks the same way again (DEC-042). Nothing its run produced goes in:
    /// QUERY-006 says none of it is presented as its answer, and half a sentence from a failed run is
    /// no more an answer to the next question than it is to its own. A run that ended done with no text
    /// is <i>not</i> such a turn: its question was asked and answered, if emptily, and the next run is
    /// told so rather than left to think it was never asked.
    /// </para>
    /// <para>
    /// Public and static so that the Fast suite assembles the conversation the way the hub does rather
    /// than with a copy of this rule beside it — a double that built it differently would hide exactly
    /// the kind of difference this method is where it is to prevent (Constitution III.9).
    /// </para>
    /// </remarks>
    public static string ConversationSoFar(Chat chat, Guid runId)
    {
        ArgumentNullException.ThrowIfNull(chat);

        return string.Join(
            "\n\n",
            chat.Turns
                .Where(turn => turn.Question.RunId != runId)
                .Select(turn => (turn, status: turn.Question.Status))
                .Where(asked => asked.status.State is QuestionState.Answered or QuestionState.NoAnswer)
                .Select(asked => $"Asked: {asked.turn.Question.Text}\n\n{Outcome(asked.turn, asked.status)}"));
    }

    // State and reason read as one status, so a turn is never rendered with a reason from a moment
    // other than its state's.
    private static string Outcome(ChatTurn turn, QuestionStatus status) =>
        status.State == QuestionState.Answered
            ? $"Answered: {turn.Answer}"
            : $"Got no answer because {ChatTurnView.ReasonFor(status.Because!.Value)}.";
}

using Grimoire.Agent;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// A question whose run ended failed: the chat says it got no answer and why, nothing the run had
/// produced is presented as its answer, and the failure holds the queue until it is acknowledged —
/// after which the question can be asked again (QUERY-006).
/// </summary>
/// <remarks>
/// <para>
/// QUERY-006 is the other end of RUNS-003. A failed question blocks exactly as a failed ingest does,
/// and the user must be able to clear it — but there is no row in the submissions list to clear it
/// from, because a question is not a submission. So the same rule is asked here of a
/// <see cref="Question"/>, through the chat's own acknowledgement.
/// </para>
/// <para>
/// Every failure below is one this test makes: a clock it moves itself, a streamed cost, a process
/// that is gone, or a surface that is not the grant. No test waits for real time — the Fast suite has
/// 15 s for the whole of it (Constitution III.7, DEC-018).
/// </para>
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "QUERY-006")]
public sealed class FailedQuestionTests
{
    private const string AboutAda = "What does the wiki say about Ada Lovelace?";

    private readonly FastHub hub = new();

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task QuestionRunFails_LeavesTheQuestionWithNoAnswer_WhenTheTimeCeilingIsReached()
    {
        var question = await hub.AskedAsync(AboutAda);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);

        NoAnswerBecause(question, RunEndedBecause.TimeCeiling);
    }

    [Fact]
    [Trait("req", "GUARD-004")]
    public async Task QuestionRunFails_LeavesTheQuestionWithNoAnswer_WhenTheCostCeilingIsReached()
    {
        var question = await hub.AskedAsync(AboutAda);

        hub.Harness.Spend(question.Id, Ceilings.Fixed.Cost);

        NoAnswerBecause(question, RunEndedBecause.CostCeiling);
    }

    [Fact]
    public async Task QuestionRunFails_LeavesTheQuestionWithNoAnswer_WhenTheAgentProcessDies()
    {
        var question = await hub.AskedAsync(AboutAda);

        hub.Harness.End(question.Id, RunOutcome.Failed, RunEndedBecause.AgentProcessDied);

        NoAnswerBecause(question, RunEndedBecause.AgentProcessDied);
    }

    [Fact]
    public async Task QuestionRunFails_LeavesTheQuestionWithNoAnswer_WhenTheToolsAreNotTheGrant()
    {
        // Said at `system/init`, before any model call: the run is over in the window between the
        // question being accepted and the agent reporting in, which is where the grant is checked
        // (GUARD-001). So this one is set before the question is asked.
        hub.Harness.ReportedSurface = [.. ToolGrant.ForQuestion, "bash"];

        var question = await hub.AskedAsync(AboutAda);

        NoAnswerBecause(question, RunEndedBecause.ToolsWereNotTheGrant);
    }

    [Fact]
    public async Task QuestionRunFails_PresentsNothingItProducedAsItsAnswer()
    {
        var question = await hub.AskedAsync(AboutAda);

        // Half a sentence from a run that then stopped at a ceiling.
        hub.Harness.Did(question.Id, new TranscriptMoment(RunMomentKind.AgentSaid, null, "She wrote the fir"));
        hub.Clock.Advance(Ceilings.Fixed.Elapsed);

        var turn = Assert.Single(hub.Chat.Turns);

        // The chat still **holds** what arrived — it is what the run produced, and the steps beside it
        // are how the user checks what happened. What it must not do is present it as the answer, and
        // that is said by the state: the question reads *got no answer*, so the page draws the reason
        // and not the prose (there is a CSS rule for it, and an E2E test on what is on the screen).
        Assert.Equal("She wrote the fir", turn.Answer);
        Assert.Equal(QuestionState.NoAnswer, question.State);
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task QuestionRunFails_StartsNothingFurther_WhileItIsUnacknowledged()
    {
        var question = await hub.AskedAsync(AboutAda);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);

        var waiting = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");

        // Accepted, and waiting: what blocks is the failure, not acceptance. A question holds the
        // queue exactly as a failed submission does (RUNS-003, QUERY-006).
        Assert.Equal(SubmissionState.Submitted, waiting.State);
        Assert.Equal([question.Id], hub.Harness.Dispatched.Select(dispatch => dispatch.SubmissionId));
    }

    [Fact]
    [Trait("req", "RUNS-003")]
    public async Task Acknowledge_StartsWhatWasWaitingBehindTheFailedQuestion()
    {
        var question = await hub.AskedAsync(AboutAda);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);

        var waiting = await hub.AcceptedAsync("Ada Lovelace wrote the first program.");

        await hub.AcknowledgeQuestionAsync(question.Id);

        // The one control the chat offers against the question that failed, and it clears the block —
        // there is no row in the submissions list to clear it from (ACCESS-003, QUERY-006).
        Assert.Equal(
            [question.Id, waiting.Id], hub.Harness.Dispatched.Select(dispatch => dispatch.SubmissionId));
    }

    [Fact]
    public async Task Ask_IsGivenARun_AfterTheFailureWasAcknowledged()
    {
        var failed = await hub.AskedAsync(AboutAda);

        hub.Clock.Advance(Ceilings.Fixed.Elapsed);
        await hub.AcknowledgeQuestionAsync(failed.Id);

        // The same question again, which is what QUERY-006 promises the user: the text is accepted and
        // it is given a run of its own, while the one that got no answer stays in the chat as it was.
        var again = await hub.AskedAsync(AboutAda);

        Assert.NotEqual(failed.Id, again.Id);
        Assert.NotNull(again.RunId);
        Assert.Equal(QuestionState.Answering, again.State);
        Assert.Equal([failed.Id, again.Id], hub.Chat.Turns.Select(turn => turn.Question.Id));
    }

    /// <summary>
    /// The question got no answer, this is why, and the turn is still in the chat to say so
    /// (QUERY-006).
    /// </summary>
    private void NoAnswerBecause(Question question, RunEndedBecause because)
    {
        Assert.Equal(QuestionState.NoAnswer, question.State);
        Assert.Equal(because, question.Because);

        // Still there: a question whose run failed is read in the chat, not taken out of it.
        Assert.Equal(question, Assert.Single(hub.Chat.Turns).Question);
    }
}

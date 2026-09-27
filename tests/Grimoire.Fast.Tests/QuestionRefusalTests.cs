using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The three refusals a question can meet, checked in the order <c>contracts/hub-http-api.md</c>
/// gives them, so that each refusal names exactly one thing — and a refused question is nowhere
/// afterwards (QUERY-003).
/// </summary>
/// <remarks>
/// The shape <see cref="SubmissionRefusalTests"/> already has, because the rule is the same rule with
/// the <em>question</em> instruction in place of the ingest one. That difference is the last test
/// here: a question is never refused for the instruction it is not given.
/// </remarks>
[Trait("level", "fast")]
[Trait("req", "QUERY-003")]
public sealed class QuestionRefusalTests
{
    private const string Text = "What does the wiki say about Ada Lovelace?";

    private readonly FastHub hub = new();

    private static readonly StartUpInputs NoQuestionInstruction =
        new(InstructionPresent: true, QuestionInstructionPresent: false, PurposeDescriptionPresent: true);

    private static readonly StartUpInputs NoPurposeDescription =
        new(InstructionPresent: true, QuestionInstructionPresent: true, PurposeDescriptionPresent: false);

    [Fact]
    public async Task Ask_IsRefused_WhenTheQuestionInstructionIsMissing()
    {
        var result = await hub.AskAsync(Text, NoQuestionInstruction);

        Assert.Equal(QuestionRefusal.QuestionInstructionMissing, result.Refused);
    }

    [Fact]
    public async Task Ask_IsRefused_WhenThePurposeDescriptionIsMissing()
    {
        var result = await hub.AskAsync(Text, NoPurposeDescription);

        // Which of the two texts is missing, and not merely that one is: that is what QUERY-003 asks
        // the user to be told.
        Assert.Equal(QuestionRefusal.PurposeDescriptionMissing, result.Refused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n  ")]
    public async Task Ask_IsRefused_WhenTheTextIsEmptyOrWhitespace(string text)
    {
        var result = await hub.AskAsync(text, StartUpInputs.BothPresent);

        Assert.Equal(QuestionRefusal.TextEmpty, result.Refused);
    }

    [Fact]
    public async Task Ask_NamesTheQuestionInstructionFirst_WhenNoneOfTheThreeIsInPlace()
    {
        var neither = new StartUpInputs(
            InstructionPresent: true, QuestionInstructionPresent: false, PurposeDescriptionPresent: false);

        // An empty question with neither text in place is refused for the question instruction: the two
        // start-up inputs come before the text, and the question instruction before the purpose
        // description, so each refusal names exactly one thing (contracts/hub-http-api.md).
        Assert.Equal(QuestionRefusal.QuestionInstructionMissing, (await hub.AskAsync("", neither)).Refused);
        Assert.Equal(QuestionRefusal.QuestionInstructionMissing, (await hub.AskAsync(Text, neither)).Refused);
        Assert.Equal(
            QuestionRefusal.PurposeDescriptionMissing, (await hub.AskAsync("", NoPurposeDescription)).Refused);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Text)]
    public async Task Ask_IsInNoChat_WhenRefused(string text)
    {
        // Whether it was refused for the text or for a missing file, a refused question is not a
        // Question: there is nothing of it afterwards to carry a state.
        foreach (var inputs in Refusing(text))
        {
            var result = await hub.AskAsync(text, inputs);

            Assert.Null(result.Accepted);
            Assert.Empty(hub.Chat.Turns);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(Text)]
    public async Task Ask_StartsNoRun_WhenRefused(string text)
    {
        foreach (var inputs in Refusing(text))
        {
            await hub.AskAsync(text, inputs);

            Assert.Empty(hub.Harness.Dispatched);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(Text)]
    public async Task Ask_IsStoredNowhere_WhenRefused(string text)
    {
        foreach (var inputs in Refusing(text))
        {
            await hub.AskAsync(text, inputs);

            Assert.Empty(hub.Store.Load());

            // Not even a run's row, which is the one thing an *accepted* question does put on disk
            // (QUERY-005): a refused question was never handed a run to write down.
            Assert.Empty(hub.Store.LoadRunsWithoutASubmission());
        }
    }

    [Fact]
    public async Task Ask_IsAccepted_WithoutTheIngestInstruction()
    {
        var withoutTheIngestInstruction = new StartUpInputs(
            InstructionPresent: false, QuestionInstructionPresent: true, PurposeDescriptionPresent: true);

        var result = await hub.AskAsync(Text, withoutTheIngestInstruction);

        // A question is refused on the *question* instruction and never on the ingest one: the text a
        // submission's run is given has nothing to do with answering a question (QUERY-003,
        // INGEST-003).
        Assert.Null(result.Refused);
        Assert.NotNull(result.Accepted);
    }

    /// <summary>The start-up inputs under which this question is refused, whatever else is in place.</summary>
    private static IEnumerable<StartUpInputs> Refusing(string text) =>
        text.Length == 0
            ? [NoQuestionInstruction, NoPurposeDescription, StartUpInputs.BothPresent]
            : [NoQuestionInstruction, NoPurposeDescription];
}

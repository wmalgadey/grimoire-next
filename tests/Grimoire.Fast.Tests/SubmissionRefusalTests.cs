using Grimoire.Hub;
using Grimoire.Runs;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The refusals that are about the submission itself: a start-up input Grimoire was never given
/// (INGEST-003) and a text with nothing in it (INGEST-004).
/// </summary>
[Trait("level", "fast")]
public sealed class SubmissionRefusalTests
{
    private readonly InMemoryAgentHarness harness = new();
    private readonly SubmissionBoard board = new(FastSuite.Clock());
    private readonly SubmissionIntake intake;

    public SubmissionRefusalTests() => intake = new SubmissionIntake(board, harness);

    private static readonly StartUpInputs NoInstruction =
        new(InstructionPresent: false, PurposeDescriptionPresent: true);

    private static readonly StartUpInputs NoPurposeDescription =
        new(InstructionPresent: true, PurposeDescriptionPresent: false);

    [Fact]
    [Trait("req", "INGEST-003")]
    public async Task Submit_IsRefused_WhenTheInstructionIsMissing()
    {
        var result = await intake.SubmitAsync("A text.", NoInstruction);

        Assert.Equal(Refusal.InstructionMissing, result.Refused);
    }

    [Fact]
    [Trait("req", "INGEST-003")]
    public async Task Submit_IsRefused_WhenThePurposeDescriptionIsMissing()
    {
        var result = await intake.SubmitAsync("A text.", NoPurposeDescription);

        // The refusal names which of the two is missing; that is what INGEST-003 asks for.
        Assert.Equal(Refusal.PurposeDescriptionMissing, result.Refused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n  ")]
    [Trait("req", "INGEST-004")]
    public async Task Submit_IsRefused_WhenTheTextIsEmptyOrWhitespace(string text)
    {
        var result = await intake.SubmitAsync(text, StartUpInputs.BothPresent);

        Assert.Equal(Refusal.TextEmpty, result.Refused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A text.")]
    [Trait("req", "INGEST-003")]
    [Trait("req", "INGEST-004")]
    public async Task Submit_StoresNothingAndStartsNoRun_WhenRefused(string text)
    {
        foreach (var inputs in new[] { NoInstruction, NoPurposeDescription, StartUpInputs.BothPresent })
        {
            var result = await intake.SubmitAsync(text, inputs);

            // Whether it was refused for the text or for a missing file, nothing is kept and no
            // run begins: a refused submission is not a Submission and carries no state.
            if (result.Refused is not null)
            {
                Assert.Empty(board.All);
                Assert.Empty(harness.Dispatched);
            }
        }
    }

    [Fact]
    [Trait("req", "INGEST-003")]
    [Trait("req", "INGEST-004")]
    public async Task Submit_NamesTheInstructionFirst_WhenSeveralAreMissing()
    {
        var neither = new StartUpInputs(InstructionPresent: false, PurposeDescriptionPresent: false);

        // An empty text with neither file in place is refused with instruction-missing: the two
        // start-up inputs come before the text, and the instruction before the purpose
        // description, so each refusal names exactly one missing file
        // (contracts/hub-http-api.md).
        Assert.Equal(Refusal.InstructionMissing, (await intake.SubmitAsync("", neither)).Refused);
        Assert.Equal(Refusal.InstructionMissing, (await intake.SubmitAsync("A text.", neither)).Refused);
        Assert.Equal(Refusal.PurposeDescriptionMissing, (await intake.SubmitAsync("", NoPurposeDescription)).Refused);
    }
}

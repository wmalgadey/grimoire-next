using Grimoire.Trace;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the gate reads out of a capability file, given the file as text.
/// </summary>
/// <remarks>
/// <para>
/// These prove Principle IV.3 rather than a requirement, so they carry no <c>req</c> trait: the
/// reader is fail-closed, because a line that opens like a requirement row and does not read as
/// one fails the read instead of being passed over, and a mis-registered requirement must not be
/// able to slip out of the gate unnoticed.
/// </para>
/// <para>
/// No directory and no file: finding the files is the registry's other half, and every judgment
/// it makes is in the half that takes text.
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class CapabilityRegistryTests
{
    private const string Header =
        """
        # INGEST

        | ID | Requirement | Proof |
        | --- | --- | --- |

        """;

    private static IReadOnlyList<Requirement> Read(string rows, string name = "ingest.md") =>
        CapabilityRegistry.Parse([new CapabilityFile(name, Header + rows)]);

    [Fact]
    public void Read_RegistersTheRequirement_WhenTheRowReadsInFull()
    {
        var requirements = Read("| INGEST-001 | Users MUST be able to submit a text. | test |\n");

        var requirement = Assert.Single(requirements);
        Assert.Equal("INGEST-001", requirement.Id);
        Assert.Equal("INGEST", requirement.Capability);
        Assert.Equal("Users MUST be able to submit a text.", requirement.Text);
        Assert.Equal(ProofKind.Test, requirement.Proof);
        Assert.False(requirement.Retired);
    }

    [Theory]
    [InlineData("| ingest-002 | A lower-case id. | test |")]
    [InlineData("| INGEST-02 | Two digits, not three. | test |")]
    [InlineData("| INGEST-002 | No proof column. |")]
    [InlineData("| INGEST-002 | A proof that is not a word. | test? |")]
    [InlineData("| INGEST-002 | Text after the last pipe. | test | and more")]
    public void Read_Fails_WhenARowDoesNotReadAsARequirementInFull(string row)
    {
        var failure = Assert.Throws<TraceInputException>(() => Read(row + "\n"));

        Assert.Contains("ingest.md line 5", failure.Message, StringComparison.Ordinal);
        Assert.Contains(row, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_PassesOverARowThatIsNotARequirement() =>
        Assert.Empty(Read("Six parts of OKF 0.2 apply; nothing beyond them is built.\n| --- | --- |\n"));

    [Fact]
    public void Read_MarksTheRequirementRetired_WhenTheRowSitsUnderRetired()
    {
        var requirements = Read("## Retired\n\n| INGEST-006 | Withdrawn. | test |\n");

        Assert.True(Assert.Single(requirements).Retired);
    }

    [Fact]
    public void Read_LeavesTheRequirementActive_WhenAHeadingAfterRetiredEndsTheSection()
    {
        var requirements = Read(
            "## Retired\n\n| INGEST-006 | Withdrawn. | test |\n\n## Requirements\n\n| INGEST-007 | Live. | test |\n");

        Assert.Equal([true, false], requirements.Select(r => r.Retired));
    }

    [Fact]
    public void Read_Fails_WhenAnIdIsRegisteredTwice()
    {
        var failure = Assert.Throws<TraceInputException>(
            () => Read("| INGEST-001 | Once. | test |\n| INGEST-001 | Twice. | test |\n"));

        Assert.Contains("INGEST-001 is registered more than once", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Fails_WhenTheSameIdIsRegisteredInTwoFiles()
    {
        var failure = Assert.Throws<TraceInputException>(() => CapabilityRegistry.Parse(
        [
            new CapabilityFile("ingest.md", Header + "| INGEST-001 | Once. | test |\n"),
            new CapabilityFile("runs.md", Header + "| INGEST-001 | Twice. | test |\n"),
        ]));

        Assert.Contains("INGEST-001 is registered more than once", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test", nameof(ProofKind.Test))]
    [InlineData("eval", nameof(ProofKind.Eval))]
    [InlineData("review", nameof(ProofKind.Review))]
    [InlineData("Review", nameof(ProofKind.Review))]
    public void Read_RegistersTheProofKind_ForEachKindTheConstitutionAllows(string proof, string expected) =>
        Assert.Equal(
            expected,
            Assert.Single(Read($"| INGEST-001 | A requirement. | {proof} |\n")).Proof.ToString());

    [Fact]
    public void Read_Fails_WhenTheProofIsNotOneOfTheThree()
    {
        var failure = Assert.Throws<TraceInputException>(
            () => Read("| INGEST-001 | A requirement. | inspection |\n"));

        Assert.Contains("INGEST-001 in ingest.md declares proof 'inspection'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_KeepsTheWholeText_WhenTheRequirementContainsAPipe() =>
        Assert.Equal(
            "Either a | or a comma.",
            Assert.Single(Read("| INGEST-001 | Either a | or a comma. | test |\n")).Text);

    [Theory]
    [InlineData("OUT-01")]
    [InlineData("DEC-001")]
    public void Capability_IsReserved_ForThePrefixesTheConstitutionKeeps(string id) =>
        Assert.True(CapabilityRegistry.IsReserved(id));

    [Theory]
    [InlineData("INGEST-001")]
    [InlineData("GUARD-004")]
    [InlineData("OUTFLOW-001")]
    public void Capability_IsNotReserved_ForACapabilityOfOurOwn(string id) =>
        Assert.False(CapabilityRegistry.IsReserved(id));

    [Theory]
    [InlineData("INGEST-001", "INGEST")]
    [InlineData("OUT-01", "OUT")]
    [InlineData("nodash", "nodash")]
    public void Capability_IsWhatStandsBeforeTheDash(string id, string capability) =>
        Assert.Equal(capability, CapabilityRegistry.CapabilityOf(id));

    [Fact]
    public void Read_Fails_WithNoCapabilityFileAtAll() =>
        // The gate's inputs are gone. Read as a registry of no requirements it would pass over
        // nothing and report success, so deleting the capability files would switch the gate off
        // instead of failing it. What it cannot read, it fails on (IV.3).
        Assert.Throws<TraceInputException>(() => CapabilityRegistry.Parse([]));
}

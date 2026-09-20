using Grimoire.Trace;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The reader is fail-closed: a line that opens like a requirement row and does not read as one
/// fails the read instead of being passed over, so a mis-registered requirement cannot slip out
/// of the gate unnoticed (Constitution IV.3).
/// </summary>
[Trait("level", "fast")]
public sealed class CapabilityRegistryTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("grimoire-capabilities-").FullName;

    private const string Header = """
        # INGEST

        | ID | Requirement | Proof |
        | --- | --- | --- |

        """;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private IReadOnlyList<Requirement> Read(string rows)
    {
        File.WriteAllText(Path.Combine(directory, "ingest.md"), Header + rows);
        return CapabilityRegistry.Read(directory);
    }

    [Fact]
    public void ARowThatReadsInFullRegistersItsRequirement()
    {
        var requirements = Read("| INGEST-001 | Users MUST be able to submit a text. | test |\n");

        var requirement = Assert.Single(requirements);
        Assert.Equal("INGEST-001", requirement.Id);
        Assert.Equal("INGEST", requirement.Capability);
        Assert.Equal(ProofKind.Test, requirement.Proof);
        Assert.False(requirement.Retired);
    }

    [Theory]
    [InlineData("| ingest-002 | A lower-case id. | test |")]
    [InlineData("| INGEST-02 | Two digits, not three. | test |")]
    [InlineData("| INGEST-002 | No proof column. |")]
    [InlineData("| INGEST-002 | A proof that is not a word. | test? |")]
    [InlineData("| INGEST-002 | Text after the last pipe. | test | and more")]
    public void ARowThatOpensLikeARequirementAndDoesNotReadAsOneFailsTheRead(string row)
    {
        var failure = Assert.Throws<TraceInputException>(() => Read(row + "\n"));

        Assert.Contains("ingest.md line 5", failure.Message, StringComparison.Ordinal);
        Assert.Contains(row, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProseAndSeparatorRowsAreNotRequirementRows()
    {
        Assert.Empty(Read("Six parts of OKF 0.2 apply; nothing beyond them is built.\n| --- | --- |\n"));
    }

    [Fact]
    public void ARequirementUnderRetiredKeepsItsIdAndIsMarkedRetired()
    {
        var requirements = Read("## Retired\n\n| INGEST-006 | Withdrawn. | test |\n");

        Assert.True(Assert.Single(requirements).Retired);
    }

    [Fact]
    public void AnIdRegisteredTwiceFailsTheRead()
    {
        var failure = Assert.Throws<TraceInputException>(
            () => Read("| INGEST-001 | Once. | test |\n| INGEST-001 | Twice. | test |\n"));

        Assert.Contains("INGEST-001 is registered more than once", failure.Message, StringComparison.Ordinal);
    }
}

using Grimoire.Trace;

namespace Grimoire.Fast.Tests;

/// <summary>
/// One test per condition of Constitution IV.3, and one for where each condition runs. The gate
/// is code we wrote, so III.8 does not exclude it; nothing here touches the filesystem or a
/// runner, because <see cref="TraceCheck.Run"/> is given the two lists it decides on.
/// </summary>
[Trait("level", "fast")]
public sealed class TraceCheckTests
{
    private static Requirement Registered(string id, ProofKind proof = ProofKind.Test, bool retired = false) =>
        new(id, CapabilityRegistry.CapabilityOf(id), $"{id} does something", proof, retired);

    private static TestMethod Test(string name, string? level, params string[] requirementIds) =>
        new("Grimoire.Fast.Tests", "Suite", name, level, requirementIds);

    [Fact]
    public void Check_Fails_WhenATestRequirementHasNoTestAndCompleteIsAsked()
    {
        var violations = TraceCheck.Run([Registered("INGEST-001")], [], complete: true);

        Assert.Equal(["INGEST-001 is proven by test and no test carries its id"], violations);
    }

    [Fact]
    public void Check_Passes_WhenATestRequirementHasNoTestAndCompleteIsNotAsked()
    {
        // Red by construction while a feature is in flight: IV.2 registers the requirement before
        // its test is written, so this condition runs where the feature lands on main and nowhere
        // else.
        Assert.Empty(TraceCheck.Run([Registered("INGEST-001")], [], complete: false));
    }

    [Fact]
    public void Check_Passes_WhenAReviewRequirementHasNoTest()
    {
        Assert.Empty(TraceCheck.Run([Registered("WIKI-001", ProofKind.Review)], [], complete: true));
    }

    [Fact]
    public void Check_Fails_WhenATestCarriesAnUnknownRetiredOrReservedId()
    {
        IReadOnlyList<Requirement> requirements = [Registered("WIKI-002"), Registered("WIKI-003", retired: true)];

        IReadOnlyList<TestMethod> tests =
        [
            Test("Unknown", "fast", "WIKI-009"),
            Test("Retired", "fast", "WIKI-003"),
            Test("Reserved", "fast", "OUT-01"),
            Test("Registered", "fast", "WIKI-002"),
        ];

        Assert.Equal(
            [
                "Grimoire.Fast.Tests: Suite.Reserved carries req \"OUT-01\", which is reserved (Constitution I.2)",
                "Grimoire.Fast.Tests: Suite.Retired carries req \"WIKI-003\", which is retired",
                "Grimoire.Fast.Tests: Suite.Unknown carries req \"WIKI-009\", which is not registered in docs/capabilities/",
            ],
            TraceCheck.Run(requirements, tests, complete: false));
    }

    [Fact]
    public void Check_Fails_WhenATestCarriesNoLevel()
    {
        var violations = TraceCheck.Run([Registered("RUNS-001")], [Test("NoLevel", null, "RUNS-001")], complete: true);

        Assert.Equal(
            ["Grimoire.Fast.Tests: Suite.NoLevel carries no level; one of fast, contract, e2e, deploy is required"],
            violations);
    }

    [Fact]
    public void Check_Fails_WhenAnE2EOrDeployTestCarriesNoRequirementId()
    {
        IReadOnlyList<TestMethod> tests = [Test("Browser", "e2e"), Test("Deployed", "deploy"), Test("InProcess", "fast")];

        Assert.Equal(
            [
                "Grimoire.Fast.Tests: Suite.Browser is e2e and carries no requirement id",
                "Grimoire.Fast.Tests: Suite.Deployed is deploy and carries no requirement id",
            ],
            TraceCheck.Run([], tests, complete: true));
    }

    [Fact]
    public void Check_Passes_WhenTheRegistryAndTheSuiteAgree()
    {
        IReadOnlyList<Requirement> requirements = [Registered("ACCESS-001"), Registered("WIKI-001", ProofKind.Review)];
        IReadOnlyList<TestMethod> tests = [Test("Submits", "e2e", "ACCESS-001")];

        Assert.Empty(TraceCheck.Run(requirements, tests, complete: true));
    }
}

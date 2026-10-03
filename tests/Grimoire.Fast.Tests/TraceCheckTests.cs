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

    private static TestMethod SignedIn(string name) =>
        new("Grimoire.Contract.Tests", "Harness", name, "contract", [], NeedsSignIn: true);

    [Fact]
    public void CompleteCheck_Fails_WhenATestRequirementHasNoTest()
    {
        var violations = TraceCheck.Run([Registered("INGEST-001")], [], complete: true);

        Assert.Equal(["INGEST-001 is proven by test and no test carries its id"], violations);
    }

    [Fact]
    public void Check_Passes_WhenATestRequirementHasNoTest()
    {
        // Red by construction while a feature is in flight: IV.2 registers the requirement before
        // its test is written, so this condition runs where the feature lands on main and nowhere
        // else.
        Assert.Empty(TraceCheck.Run([Registered("INGEST-001")], [], complete: false));
    }

    [Fact]
    public void CompleteCheck_Passes_WhenAReviewRequirementHasNoTest()
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
    public void Check_Fails_WhenMoreThanFourTestsNeedTheSignIn()
    {
        // DEC-021: the real CLI with the owner's sign-in, at most four tests — each one a real run on
        // the owner's subscription, and none of them run in CI.
        IReadOnlyList<TestMethod> tests = [SignedIn("A"), SignedIn("B"), SignedIn("C"), SignedIn("D"), SignedIn("E")];

        Assert.Equal(
            [
                "5 tests carry requires=signin, and DEC-021 allows at most 4: "
                + "Harness.A, Harness.B, Harness.C, Harness.D, Harness.E",
            ],
            TraceCheck.Run([], tests, complete: false));
    }

    [Fact]
    public void Check_Passes_WhenFourTestsNeedTheSignIn()
    {
        IReadOnlyList<TestMethod> tests = [SignedIn("A"), SignedIn("B"), SignedIn("C"), SignedIn("D")];

        Assert.Empty(TraceCheck.Run([], tests, complete: false));
    }

    private static readonly DateTimeOffset Built = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] OneSuite = ["Grimoire.Fast.Tests"];

    private static BuiltAssembly Assembly(string configuration, DateTimeOffset builtAt) =>
        new("Grimoire.Fast.Tests", configuration, $"tests/Grimoire.Fast.Tests/bin/{configuration}/net10.0/Grimoire.Fast.Tests.dll", builtAt);

    private static SourceFile Source(DateTimeOffset writtenAt) =>
        new("Grimoire.Fast.Tests", "tests/Grimoire.Fast.Tests/ChatTests.cs", writtenAt);

    [Fact]
    public void Check_RefusesToRead_WhenTheAssemblyIsOlderThanASourceOfItsSuite()
    {
        // #70 wrote docs/trace.md from Release assemblies older than the tests they claimed to list,
        // and the gate was green: what it read was not the tree it was asked about.
        var refused = Assert.Throws<TraceInputException>(() => RepositoryLayout.Choose(
            OneSuite,
            [Assembly("Release", Built), Assembly("Debug", Built)],
            [Source(Built.AddMinutes(1))],
            configuration: null));

        // The message names the build and the path, so the reader knows what to rebuild and why.
        Assert.Contains("Release", refused.Message, StringComparison.Ordinal);
        Assert.Contains("tests/Grimoire.Fast.Tests/bin/Release/net10.0/Grimoire.Fast.Tests.dll", refused.Message, StringComparison.Ordinal);
        Assert.Contains("tests/Grimoire.Fast.Tests/ChatTests.cs", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_ReadsRelease_WhenItIsFresh()
    {
        var chosen = RepositoryLayout.Choose(
            OneSuite,
            [Assembly("Release", Built), Assembly("Debug", Built)],
            [Source(Built.AddMinutes(-1))],
            configuration: null);

        Assert.Contains("/Release/", Assert.Single(chosen).Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_ReadsDebug_WhereReleaseIsOlderThanASource()
    {
        // Release is preferred only where it is fresh. A Debug build made after the last edit is the
        // tree as it stands.
        var chosen = RepositoryLayout.Choose(
            OneSuite,
            [Assembly("Release", Built), Assembly("Debug", Built.AddMinutes(2))],
            [Source(Built.AddMinutes(1))],
            configuration: null);

        Assert.Contains("/Debug/", Assert.Single(chosen).Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_RefusesToRead_WhenTheConfigurationAskedForIsOlderThanASource()
    {
        Assert.Throws<TraceInputException>(() => RepositoryLayout.Choose(
            OneSuite,
            [Assembly("Release", Built.AddMinutes(2)), Assembly("Debug", Built)],
            [Source(Built.AddMinutes(1))],
            configuration: "Debug"));
    }

    [Fact]
    public void Check_Passes_WhenEveryTestMatchesTheRegistry()
    {
        IReadOnlyList<Requirement> requirements = [Registered("ACCESS-001"), Registered("WIKI-001", ProofKind.Review)];
        IReadOnlyList<TestMethod> tests = [Test("Submits", "e2e", "ACCESS-001")];

        Assert.Empty(TraceCheck.Run(requirements, tests, complete: true));
    }
}

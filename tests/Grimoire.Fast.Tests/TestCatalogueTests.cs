using Grimoire.Trace;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the gate reads off a built test assembly — read off this one.
/// </summary>
/// <remarks>
/// <para>
/// These prove Principle IV.3 rather than a requirement, so they carry no <c>req</c> trait. IV.3
/// wants one deterministic check that runs no test and asks no runner; the catalogue is the half
/// of it that reads <c>level</c> and <c>req</c> out of assembly metadata, and the assembly it is
/// pointed at here is the Fast suite's own. That is the honest fixture: the traits it finds are
/// the traits the tests beside it actually carry, so a change to how a trait is written shows up
/// here rather than in CI.
/// </para>
/// <para>
/// The catalogue opens the assembly with <c>MetadataLoadContext</c>. No test in it is run, which
/// is why reading this suite from inside itself terminates.
/// </para>
/// </remarks>
[Trait("level", "fast")]
public sealed class TestCatalogueTests
{
    private const string Suite = "Grimoire.Fast.Tests";

    /// <summary>
    /// Read once per test, not once per process. A catalogue cached in a static field would be
    /// whatever the first test in the run produced, and every test after it would be asserting
    /// on that first reading rather than on its own.
    /// </summary>
    private readonly IReadOnlyList<TestMethod> catalogue =
        TestCatalogue.Read([new TestAssembly(Suite, typeof(TestCatalogueTests).Assembly.Location)]);

    private TestMethod Find(string typeName, string methodName) =>
        Assert.Single(catalogue, t => t.TypeName == $"{Suite}.{typeName}" && t.MethodName == methodName);

    [Fact]
    public void Read_FindsTheLevelTheClassCarries() =>
        Assert.Equal("fast", Find(nameof(TestCatalogueTests), nameof(Read_FindsTheLevelTheClassCarries)).Level);

    [Fact]
    public void Read_FindsEveryTestInTheAssembly() =>
        // Every test here carries its level on the class, so a test with none would be a test the
        // catalogue failed to reach rather than one that is really unlabelled.
        Assert.All(catalogue, test => Assert.Equal("fast", test.Level));

    [Fact]
    public void Read_FindsTheRequirementIdTheMethodCarries() =>
        Assert.Equal(
            ["GUARD-001"],
            Find(nameof(AgentTranscriptTests), nameof(AgentTranscriptTests.Init_ReportsTheAgentIn_WhenTheSurfaceIsTheGrant))
                .RequirementIds);

    [Fact]
    public void Read_FindsNoRequirementId_WhenTheTestCarriesNone() =>
        Assert.Empty(Find(nameof(TestCatalogueTests), nameof(Read_FindsNoRequirementId_WhenTheTestCarriesNone)).RequirementIds);

    [Fact]
    public void Read_FindsATheory_AsOneTest() =>
        // A [Theory] is one method however many rows it carries: the gate counts methods, and a
        // row is not a test that can carry a trait of its own.
        Assert.Equal(
            ["GUARD-002"],
            Find(nameof(ToolGrantTests), nameof(ToolGrantTests.Grant_LeavesOutDeletingAndMoving)).RequirementIds);

    [Fact]
    public void Read_NamesTheSuiteItWasGiven() =>
        Assert.All(catalogue, test => Assert.Equal(Suite, test.Suite));

    [Fact]
    public void Read_NamesATestByItsTypeAndMethod() =>
        Assert.Equal(
            $"{Suite}.{nameof(TestCatalogueTests)}.{nameof(Read_NamesATestByItsTypeAndMethod)}",
            Find(nameof(TestCatalogueTests), nameof(Read_NamesATestByItsTypeAndMethod)).DisplayName);

    [Fact]
    public void Read_PassesOverAMethodThatIsNotATest() =>
        Assert.DoesNotContain(
            catalogue,
            test => test.TypeName == $"{Suite}.{nameof(TestCatalogueTests)}" && test.MethodName == nameof(Find));
}

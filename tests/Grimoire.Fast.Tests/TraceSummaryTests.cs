using Grimoire.Trace;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What the <c>summary</c> verb counts, given the two lists the gate reads. This proves the
/// counting rather than a requirement, so it carries no <c>req</c> trait: the count is a
/// measurement of the repository and no requirement of the product rests on it.
/// </summary>
/// <remarks>
/// One test, because the count is one answer. The input below makes every part of it non-trivial:
/// a requirement of each proof kind, a retired one that keeps its id, a <c>test</c> requirement no
/// test carries, and a test carrying an id twice.
/// </remarks>
[Trait("level", "fast")]
public sealed class TraceSummaryTests
{
    [Fact]
    public void Summarise_CountsTheActiveRequirements()
    {
        IReadOnlyList<Requirement> requirements =
        [
            new("INGEST-001", "INGEST", "A text is submitted.", ProofKind.Test, Retired: false),
            new("INGEST-002", "INGEST", "A second text is refused.", ProofKind.Test, Retired: false),
            new("WIKI-001", "WIKI", "A page carries its provenance.", ProofKind.Eval, Retired: false),
            new("WIKI-002", "WIKI", "The wiki is the user's own repository.", ProofKind.Review, Retired: false),
            new("WIKI-003", "WIKI", "Undo is the user's git history.", ProofKind.Test, Retired: true),
        ];

        IReadOnlyList<TestMethod> tests =
        [
            new("Grimoire.Fast.Tests", "SubmissionTests", "Submit_IsAccepted", "fast", ["INGEST-001"]),
            new("Grimoire.E2E.Tests", "SubmitTextTests", "Submit_IsAccepted", "e2e", ["INGEST-001", "WIKI-003"]),
        ];

        var counts = TraceSummary.Count(requirements, tests);

        Assert.Equal(4, counts.Requirements);
        Assert.Equal(new ProofCounts(Test: 2, Eval: 1, Review: 1), counts.ByProof);
        Assert.Equal(1, counts.TestRequirementsWithATest);
    }
}

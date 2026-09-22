using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.Trace;

/// <summary>How many requirements each proof kind of Constitution III.1 carries.</summary>
internal sealed record ProofCounts(int Test, int Eval, int Review);

/// <summary>
/// What the third verb prints: the requirements registered in <c>docs/capabilities/</c>, how they
/// are proven, and how many of the <c>test</c> ones a test actually carries the id of.
/// </summary>
internal sealed record RequirementCounts(int Requirements, ProofCounts ByProof, int TestRequirementsWithATest);

/// <summary>
/// The <c>summary</c> verb. Counts what the gate already reads and writes nothing — neither a
/// file nor a verdict: a count is a measurement, and no value of it makes this command fail.
/// </summary>
/// <remarks>
/// Retired requirements are not counted. They keep their id (Constitution IV.2) and are no longer
/// requirements of the system, which is also how <see cref="TraceCheck"/> reads them.
/// </remarks>
internal static class TraceSummary
{
    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public static RequirementCounts Count(IReadOnlyList<Requirement> requirements, IReadOnlyList<TestMethod> tests)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(tests);

        var active = requirements.Where(r => !r.Retired).ToArray();
        var carried = tests.SelectMany(t => t.RequirementIds).ToHashSet(StringComparer.Ordinal);

        return new RequirementCounts(
            active.Length,
            new ProofCounts(
                Array.FindAll(active, r => r.Proof == ProofKind.Test).Length,
                Array.FindAll(active, r => r.Proof == ProofKind.Eval).Length,
                Array.FindAll(active, r => r.Proof == ProofKind.Review).Length),
            Array.FindAll(active, r => r.Proof == ProofKind.Test && carried.Contains(r.Id)).Length);
    }

    public static string Render(IReadOnlyList<Requirement> requirements, IReadOnlyList<TestMethod> tests) =>
        JsonSerializer.Serialize(Count(requirements, tests), Format);
}

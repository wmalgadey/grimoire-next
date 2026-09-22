using System.Globalization;
using System.Text;

namespace Grimoire.Trace;

/// <summary>
/// The single documented command that writes <c>docs/trace.md</c> (Constitution IV.4): requirement,
/// proof kind, tests, level, status.
/// </summary>
internal static class TraceDocument
{
    public static string Render(IReadOnlyList<Requirement> requirements, IReadOnlyList<TestMethod> tests)
    {
        var document = new StringBuilder();

        document.AppendLine("# Traceability");
        document.AppendLine();
        document.AppendLine("<!-- Written by `dotnet run --project tools/Grimoire.Trace -- write`. Do not edit by hand. -->");
        document.AppendLine();
        document.AppendLine("Every registered requirement, how it is proven, and what proves it. `trace-check` is the");
        document.AppendLine("gate; this file is the readable form of the same two inputs.");
        document.AppendLine();
        document.AppendLine("Status is `proven` when a requirement proven by test has at least one test carrying its id,");
        document.AppendLine("`unproven` when it has none, and `by review` or `by eval` for the other two proof kinds,");
        document.AppendLine("which no test can carry.");

        foreach (var capability in requirements.Select(r => r.Capability).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            document.AppendLine();
            document.AppendLine(CultureInfo.InvariantCulture, $"## {capability}");
            document.AppendLine();
            document.AppendLine("| Requirement | Proof | Tests | Level | Status |");
            document.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (var requirement in requirements
                .Where(r => r.Capability.Equals(capability, StringComparison.Ordinal))
                .OrderBy(r => r.Retired)
                .ThenBy(r => r.Id, StringComparer.Ordinal))
            {
                var proving = tests
                    .Where(t => t.RequirementIds.Contains(requirement.Id, StringComparer.Ordinal))
                    .OrderBy(t => t.DisplayName, StringComparer.Ordinal)
                    .ToArray();

                document.AppendLine(CultureInfo.InvariantCulture, $"| {requirement.Id} | {Proof(requirement.Proof)} | {Tests(proving)} | {Levels(proving)} | {Status(requirement, proving)} |");
            }
        }

        var unattached = tests.Where(t => t.RequirementIds.Count == 0)
            .OrderBy(t => t.Suite, StringComparer.Ordinal)
            .ThenBy(t => t.DisplayName, StringComparer.Ordinal)
            .ToArray();

        if (unattached.Length > 0)
        {
            document.AppendLine();
            document.AppendLine("## Tests carrying no requirement id");
            document.AppendLine();
            document.AppendLine("Allowed at Fast and Contract (Constitution III.5); an E2E or Deploy test here fails the gate.");
            document.AppendLine();
            document.AppendLine("| Test | Suite | Level |");
            document.AppendLine("| --- | --- | --- |");

            foreach (var test in unattached)
            {
                document.AppendLine(CultureInfo.InvariantCulture, $"| `{test.DisplayName}` | {test.Suite} | {test.Level ?? "—"} |");
            }
        }

        return document.ToString();
    }

    private static string Proof(ProofKind proof) => proof.ToString().ToLowerInvariant();

    private static string Tests(TestMethod[] proving) =>
        proving.Length == 0 ? "—" : string.Join("<br>", proving.Select(t => $"`{t.DisplayName}`"));

    private static string Levels(TestMethod[] proving)
    {
        var levels = proving.Select(t => t.Level ?? "—").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return levels.Length == 0 ? "—" : string.Join(", ", levels);
    }

    private static string Status(Requirement requirement, TestMethod[] proving) => requirement switch
    {
        { Retired: true } => "retired",
        { Proof: ProofKind.Review } => "by review",
        { Proof: ProofKind.Eval } => "by eval",
        _ => proving.Length > 0 ? "proven" : "unproven",
    };
}

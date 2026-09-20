namespace Grimoire.Trace;

/// <summary>
/// The gate. Constitution IV.3 names four rules and this check has those four and no others. It
/// writes nothing.
/// </summary>
internal static class TraceCheck
{
    public static IReadOnlyList<string> Run(IReadOnlyList<Requirement> requirements, IReadOnlyList<TestMethod> tests)
    {
        var violations = new List<string>();
        var active = requirements.Where(r => !r.Retired).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var retired = requirements.Where(r => r.Retired).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        // 1. A `test` requirement with no test carrying its id.
        foreach (var requirement in active.Values.Where(r => r.Proof == ProofKind.Test).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!tests.Any(t => t.RequirementIds.Contains(requirement.Id, StringComparer.Ordinal)))
            {
                violations.Add($"{requirement.Id} is proven by test and no test carries its id");
            }
        }

        foreach (var test in tests.OrderBy(t => t.Suite, StringComparer.Ordinal).ThenBy(t => t.DisplayName, StringComparer.Ordinal))
        {
            // 2. A test carrying an unknown, retired or reserved id.
            foreach (var id in test.RequirementIds)
            {
                var reason =
                    CapabilityRegistry.IsReserved(id) ? "is reserved (Constitution I.2)"
                    : retired.Contains(id) ? "is retired"
                    : !active.ContainsKey(id) ? "is not registered in docs/capabilities/"
                    : null;

                if (reason is not null)
                {
                    violations.Add($"{test.Suite}: {test.DisplayName} carries req \"{id}\", which {reason}");
                }
            }

            // 3. A test with no level.
            if (test.Level is null)
            {
                violations.Add(
                    $"{test.Suite}: {test.DisplayName} carries no level; one of {string.Join(", ", TestCatalogue.Levels)} is required");
            }

            // 4. An E2E or Deploy test with no requirement id.
            if (test.Level is "e2e" or "deploy" && test.RequirementIds.Count == 0)
            {
                violations.Add($"{test.Suite}: {test.DisplayName} is {test.Level} and carries no requirement id");
            }
        }

        return violations;
    }
}

namespace Grimoire.Trace;

/// <summary>
/// The gate. Constitution IV.3 names four conditions, and this check has those four and one more:
/// DEC-021's budget of four tests that need the owner's sign-in. It writes nothing.
/// </summary>
/// <remarks>
/// <para>
/// Of IV.3's four, three hold at any moment and run on every push. The fourth — a <c>test</c> requirement
/// with no test — is red by construction while a feature is in flight, because a requirement is
/// registered before its test is written (IV.2), so IV.3 applies it where a feature lands on main.
/// That is <see cref="Run"/> with <c>complete</c> set, which CI calls on a pull request against
/// main and nowhere else.
/// </para>
/// <para>
/// The fifth is DEC-021's own "at most four", which until now only a reader could hold a class to:
/// a test that needs the sign-in is a real run on the owner's subscription and is never run in CI,
/// so one more of them is a cost nothing else would notice. It runs on every push.
/// </para>
/// </remarks>
internal static class TraceCheck
{
    public static IReadOnlyList<string> Run(IReadOnlyList<Requirement> requirements, IReadOnlyList<TestMethod> tests, bool complete)
    {
        var violations = new List<string>();
        var active = requirements.Where(r => !r.Retired).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var retired = requirements.Where(r => r.Retired).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        // 1. A `test` requirement with no test carrying its id — where the feature lands on main.
        if (complete)
        {
            foreach (var requirement in active.Values.Where(r => r.Proof == ProofKind.Test).OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                if (!tests.Any(t => t.RequirementIds.Contains(requirement.Id, StringComparer.Ordinal)))
                {
                    violations.Add($"{requirement.Id} is proven by test and no test carries its id");
                }
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

        // 5. More tests needing the owner's sign-in than DEC-021 allows.
        var signedIn = tests.Where(t => t.NeedsSignIn).Select(t => t.DisplayName).Order(StringComparer.Ordinal).ToList();

        if (signedIn.Count > SignedInBudget)
        {
            violations.Add(
                $"{signedIn.Count} tests carry requires=signin, and DEC-021 allows at most {SignedInBudget}: "
                + string.Join(", ", signedIn));
        }

        return violations;
    }

    /// <summary>DEC-021: the real CLI with the owner's real sign-in, at most four tests.</summary>
    private const int SignedInBudget = 4;
}

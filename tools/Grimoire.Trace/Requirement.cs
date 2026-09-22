namespace Grimoire.Trace;

/// <summary>How a requirement is proven. Constitution III.1 allows these three and no others.</summary>
internal enum ProofKind
{
    Test,
    Eval,
    Review,
}

/// <summary>
/// One requirement as <c>docs/capabilities/</c> registers it. <paramref name="Retired"/> marks a
/// requirement that has been removed but keeps its id (Constitution IV.2).
/// </summary>
internal sealed record Requirement(string Id, string Capability, string Text, ProofKind Proof, bool Retired);

/// <summary>One test method, as its metadata describes it.</summary>
internal sealed record TestMethod(string Suite, string TypeName, string MethodName, string? Level, IReadOnlyList<string> RequirementIds)
{
    public string DisplayName => $"{TypeName}.{MethodName}";
}

namespace Grimoire.Egress;

/// <summary>
/// Where the model route gets the credential it attaches (ADR-0012).
/// </summary>
/// <remarks>
/// A static value today. Credential refresh, usage accounting, per-run quotas and upstream failover
/// are deliberately <b>not built</b> (contracts/deployment.md, "Does not build"); a refreshing
/// credential replaces this class's body, and the model route that asks it for
/// <see cref="Current"/> does not change. It is a class and not an interface because there is one
/// implementation and nothing external behind it (constitution V.4).
/// </remarks>
public sealed class CredentialProvider(string credential)
{
    /// <summary>The credential to attach to the next upstream request.</summary>
    public string Current() => credential;
}

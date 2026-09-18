using System.Diagnostics;

namespace Grimoire.Egress;

/// <summary>The two shapes an upstream credential comes in, and the header each is attached under.</summary>
public enum ModelCredentialKind
{
    /// <summary>An Anthropic API key, attached as <c>x-api-key</c>.</summary>
    ApiKey,

    /// <summary>An OAuth-style access token, attached as <c>Authorization: Bearer</c>.</summary>
    AuthToken,
}

/// <summary>
/// The upstream credential the model route attaches (ADR-0010). Exactly one kind is configured;
/// which one determines the header, not just the value.
/// </summary>
public sealed record ModelCredential(ModelCredentialKind Kind, string Value)
{
    /// <summary>The header name and value to attach to the upstream request.</summary>
    public (string Name, string Value) AsHeader() => Kind switch
    {
        ModelCredentialKind.ApiKey => ("x-api-key", Value),
        ModelCredentialKind.AuthToken => ("Authorization", $"Bearer {Value}"),
        _ => throw new UnreachableException($"Unknown {nameof(ModelCredentialKind)}: {Kind}."),
    };
}

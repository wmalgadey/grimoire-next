namespace Grimoire.Egress;

/// <summary>
/// The proxy's whole configuration, read once from the environment at the composition root
/// (contracts/deployment.md, "Egress proxy"). A missing variable fails startup loudly.
/// </summary>
/// <param name="ModelUpstream">
/// <c>GRIMOIRE_EGRESS_MODEL_UPSTREAM</c> — the one upstream the model route reaches. The allowlist
/// is this single value: there is no second destination to configure.
/// </param>
/// <param name="Credential">
/// The upstream credential, from exactly one of <c>GRIMOIRE_EGRESS_MODEL_API_KEY</c> (attached as
/// <c>x-api-key</c>) or <c>GRIMOIRE_EGRESS_MODEL_AUTH_TOKEN</c> (attached as
/// <c>Authorization: Bearer</c>). This process is its only holder in the deployment (ADR-0010).
/// </param>
/// <param name="InternalToken">
/// <c>GRIMOIRE_EGRESS_INTERNAL_TOKEN</c> — the opaque token callers present on the model route; the
/// hub's <c>GRIMOIRE_MODEL_TOKEN</c> carries the same value.
/// </param>
public sealed record EgressConfiguration(Uri ModelUpstream, ModelCredential Credential, string InternalToken)
{
    /// <summary>Reads the configuration, naming every missing or unusable variable at once.</summary>
    /// <exception cref="InvalidOperationException">Anything required is missing or unusable.</exception>
    public static EgressConfiguration Read(Func<string, string?> lookup)
    {
        var problems = new List<string>();

        var upstream = Required(lookup, "GRIMOIRE_EGRESS_MODEL_UPSTREAM", problems);
        var credential = ReadCredential(lookup, problems);
        var token = Required(lookup, "GRIMOIRE_EGRESS_INTERNAL_TOKEN", problems);

        Uri? upstreamUri = null;
        if (upstream is not null
            && (!Uri.TryCreate(upstream, UriKind.Absolute, out upstreamUri) || upstreamUri.Scheme is not ("http" or "https")))
        {
            problems.Add($"GRIMOIRE_EGRESS_MODEL_UPSTREAM must be an absolute http(s) URL; it is '{upstream}'.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The egress proxy cannot start. Fix the following environment variables and start it again:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(problem => $"  - {problem}")));
        }

        return new EgressConfiguration(upstreamUri!, credential!, token!);
    }

    private static ModelCredential? ReadCredential(Func<string, string?> lookup, List<string> problems)
    {
        var apiKey = lookup("GRIMOIRE_EGRESS_MODEL_API_KEY");
        var authToken = lookup("GRIMOIRE_EGRESS_MODEL_AUTH_TOKEN");
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var hasAuthToken = !string.IsNullOrWhiteSpace(authToken);

        if (hasApiKey && hasAuthToken)
        {
            problems.Add(
                "Set exactly one of GRIMOIRE_EGRESS_MODEL_API_KEY or GRIMOIRE_EGRESS_MODEL_AUTH_TOKEN, not both.");
            return null;
        }

        if (hasApiKey)
        {
            return new ModelCredential(ModelCredentialKind.ApiKey, apiKey!);
        }

        if (hasAuthToken)
        {
            return new ModelCredential(ModelCredentialKind.AuthToken, authToken!);
        }

        problems.Add(
            "One of GRIMOIRE_EGRESS_MODEL_API_KEY or GRIMOIRE_EGRESS_MODEL_AUTH_TOKEN is required and neither is set.");
        return null;
    }

    private static string? Required(Func<string, string?> lookup, string name, List<string> problems)
    {
        var value = lookup(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{name} is required and is not set.");
            return null;
        }

        return value;
    }
}

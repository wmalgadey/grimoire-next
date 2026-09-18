using System.Globalization;

namespace Grimoire.Hub;

/// <summary>A required variable is missing, blank, or not a value the hub can use.</summary>
/// <remarks>
/// Thrown during startup, before the hub serves anything: a replica that cannot work should not
/// boot into a state where it looks like it can (contracts/deployment.md "Startup", step 1).
/// </remarks>
public sealed class ConfigurationException(string message) : Exception(message);

/// <summary>How the hub writes its log to stdout.</summary>
public enum LogFormat
{
    /// <summary>One JSON event per line: the transport for every declared signal.</summary>
    Json,

    /// <summary>Human-readable lines, for a hub run by hand without a log toolchain.</summary>
    Text,
}

/// <summary>
/// Everything the hub is configured with, read from environment variables and nothing else
/// (contracts/deployment.md "Environment contract"). There is deliberately no settings
/// abstraction over this: configuration is read once at the composition root (constitution VII.1).
/// </summary>
/// <param name="WikiRepositoryPath">
/// <c>GRIMOIRE_WIKI_REPO</c> — the git repository, checked out on <c>main</c>, on a volume.
/// </param>
/// <param name="StateDatabasePath">
/// <c>GRIMOIRE_STATE_DB</c> — the SQLite file, on a volume, local block storage only.
/// </param>
/// <param name="ModelBaseUrl">
/// <c>GRIMOIRE_MODEL_BASE_URL</c> — the proxy's model route, handed to the runner as
/// <c>ANTHROPIC_BASE_URL</c>.
/// </param>
/// <param name="ModelToken">
/// <c>GRIMOIRE_MODEL_TOKEN</c> — an <b>opaque internal token</b> for the proxy. No process in this
/// deployment holds an upstream model credential (ADR-0010).
/// </param>
/// <param name="InstructionPath">
/// <c>GRIMOIRE_INSTRUCTION</c> — the instruction file the runner loads; the only judgment in the
/// system (constitution I.1).
/// </param>
/// <param name="RunMaxToolCalls">
/// <c>GRIMOIRE_RUN_MAX_TOOL_CALLS</c> — the tool-call half of the run limit (FR-009).
/// </param>
/// <param name="RunMaxElapsedMs">
/// <c>GRIMOIRE_RUN_MAX_ELAPSED_MS</c> — the elapsed half of the run limit (FR-009).
/// </param>
/// <param name="FetchProxy">
/// <c>GRIMOIRE_FETCH_PROXY</c> — the URL of the proxy's fetch route used by URL retrieval (FR-003),
/// e.g. <c>http://egress:8080/fetch</c>; the hub addresses it as <c>?url=…</c>. Required in
/// containers, absent when the hub runs as a plain process on a developer machine.
/// </param>
/// <param name="LogFormat">
/// <c>GRIMOIRE_LOG_FORMAT</c> — <c>json</c> (the default) or <c>text</c>, for reading the log by
/// eye when the hub runs without a container or log toolchain.
/// </param>
public sealed record HubConfiguration(
    string WikiRepositoryPath,
    string StateDatabasePath,
    string ModelBaseUrl,
    string ModelToken,
    string InstructionPath,
    int RunMaxToolCalls,
    int RunMaxElapsedMs,
    string? FetchProxy,
    LogFormat LogFormat)
{
    /// <summary>The instruction file the hub ships with, when the environment names no other.</summary>
    public const string DefaultInstructionPath = "src/instructions/ingest.md";

    /// <summary>
    /// Reads the configuration, naming <b>every</b> missing or unusable variable at once — an
    /// operator fixing a deployment should not have to restart once per mistake.
    /// </summary>
    /// <param name="lookup">The environment. Injected so the rule is testable without a process.</param>
    /// <exception cref="ConfigurationException">Anything required is missing or unusable.</exception>
    public static HubConfiguration Read(Func<string, string?> lookup)
    {
        var problems = new List<string>();

        var wikiRepo = Required(lookup, "GRIMOIRE_WIKI_REPO", problems);
        var stateDb = Required(lookup, "GRIMOIRE_STATE_DB", problems);
        var modelBaseUrl = Required(lookup, "GRIMOIRE_MODEL_BASE_URL", problems);
        var modelToken = Required(lookup, "GRIMOIRE_MODEL_TOKEN", problems);

        var instruction = Optional(lookup, "GRIMOIRE_INSTRUCTION") ?? DefaultInstructionPath;
        var maxToolCalls = PositiveInteger(lookup, "GRIMOIRE_RUN_MAX_TOOL_CALLS", DefaultRunMaxToolCalls, problems);
        var maxElapsedMs = PositiveInteger(lookup, "GRIMOIRE_RUN_MAX_ELAPSED_MS", DefaultRunMaxElapsedMs, problems);
        var fetchProxy = Optional(lookup, "GRIMOIRE_FETCH_PROXY");
        if (fetchProxy is not null
            && (!Uri.TryCreate(fetchProxy, UriKind.Absolute, out var fetchRoute) || fetchRoute.Scheme is not ("http" or "https")))
        {
            problems.Add($"GRIMOIRE_FETCH_PROXY must be the absolute http(s) URL of the proxy's fetch route; it is '{fetchProxy}'.");
        }

        var logFormat = ReadLogFormat(lookup, problems);

        if (problems.Count > 0)
        {
            throw new ConfigurationException(
                "The hub cannot start. Fix the following environment variables and start it again:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(problem => $"  - {problem}")));
        }

        return new HubConfiguration(
            wikiRepo!, stateDb!, modelBaseUrl!, modelToken!, instruction, maxToolCalls, maxElapsedMs, fetchProxy, logFormat);
    }

    /// <summary>Reads the configuration from this process's environment.</summary>
    public static HubConfiguration FromEnvironment() =>
        Read(Environment.GetEnvironmentVariable);

    // Ceilings generous enough that a real ingest finishes, tight enough that a never-stopping
    // run ends. Both halves are configuration, not specification (FR-009).
    private const int DefaultRunMaxToolCalls = 40;
    private const int DefaultRunMaxElapsedMs = 600_000;

    private static string? Required(Func<string, string?> lookup, string name, List<string> problems)
    {
        var value = Optional(lookup, name);
        if (value is null)
        {
            problems.Add($"{name} is required and is not set.");
        }

        return value;
    }

    private static string? Optional(Func<string, string?> lookup, string name)
    {
        var value = lookup(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static LogFormat ReadLogFormat(Func<string, string?> lookup, List<string> problems)
    {
        const string name = "GRIMOIRE_LOG_FORMAT";
        switch (Optional(lookup, name)?.Trim().ToLowerInvariant())
        {
            case null or "json":
                return LogFormat.Json;
            case "text":
                return LogFormat.Text;
            default:
                problems.Add($"{name} must be 'json' or 'text'; it is '{lookup(name)}'.");
                return LogFormat.Json;
        }
    }

    private static int PositiveInteger(Func<string, string?> lookup, string name, int fallback, List<string> problems)
    {
        var value = Optional(lookup, name);
        if (value is null)
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            problems.Add($"{name} must be a positive whole number; it is '{value}'.");
            return fallback;
        }

        return parsed;
    }
}

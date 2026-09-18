using Grimoire.Hub;

namespace Grimoire.Tests.Hub;

/// <summary>
/// T030. Configuration is environment variables only, read at the composition root with no
/// settings abstraction over it (constitution VII.1, contracts/deployment.md "Environment
/// contract"). A missing required variable fails fast and loudly at startup rather than producing
/// a replica that boots and then cannot work.
/// </summary>
public sealed class ConfigurationTests
{
    private static Dictionary<string, string?> Complete() => new()
    {
        ["GRIMOIRE_WIKI_REPO"] = "/srv/wiki",
        ["GRIMOIRE_STATE_DB"] = "/srv/state/grimoire.db",
        ["GRIMOIRE_MODEL_BASE_URL"] = "http://egress:8080/v1",
        ["GRIMOIRE_MODEL_TOKEN"] = "an-opaque-internal-token",
    };

    private static HubConfiguration Read(IDictionary<string, string?> environment) =>
        HubConfiguration.Read(name => environment.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void ReadsEveryRequiredVariableFromTheEnvironment()
    {
        var configuration = Read(Complete());

        Assert.Equal("/srv/wiki", configuration.WikiRepositoryPath);
        Assert.Equal("/srv/state/grimoire.db", configuration.StateDatabasePath);
        Assert.Equal("http://egress:8080/v1", configuration.ModelBaseUrl);
        Assert.Equal("an-opaque-internal-token", configuration.ModelToken);
    }

    [Theory]
    [InlineData("GRIMOIRE_WIKI_REPO")]
    [InlineData("GRIMOIRE_STATE_DB")]
    [InlineData("GRIMOIRE_MODEL_BASE_URL")]
    [InlineData("GRIMOIRE_MODEL_TOKEN")]
    public void FailsFastAndLoudlyWhenARequiredVariableIsMissing(string missing)
    {
        var environment = Complete();
        environment.Remove(missing);

        var failure = Assert.Throws<ConfigurationException>(() => Read(environment));

        // Loudly: the message names the variable an operator has to set.
        Assert.Contains(missing, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsABlankRequiredVariableAsMissing(string blank)
    {
        var environment = Complete();
        environment["GRIMOIRE_WIKI_REPO"] = blank;

        var failure = Assert.Throws<ConfigurationException>(() => Read(environment));

        Assert.Contains("GRIMOIRE_WIKI_REPO", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesEveryMissingVariableAtOnceRatherThanOneAtATime()
    {
        var environment = Complete();
        environment.Remove("GRIMOIRE_STATE_DB");
        environment.Remove("GRIMOIRE_MODEL_TOKEN");

        var failure = Assert.Throws<ConfigurationException>(() => Read(environment));

        Assert.Contains("GRIMOIRE_STATE_DB", failure.Message, StringComparison.Ordinal);
        Assert.Contains("GRIMOIRE_MODEL_TOKEN", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultsTheInstructionPathToTheVersionedIngestInstruction()
    {
        var configuration = Read(Complete());

        Assert.Equal("src/instructions/ingest.md", configuration.InstructionPath);
    }

    [Fact]
    public void TakesTheInstructionPathFromTheEnvironmentWhenGiven()
    {
        var environment = Complete();
        environment["GRIMOIRE_INSTRUCTION"] = "/srv/instructions/ingest.md";

        Assert.Equal("/srv/instructions/ingest.md", Read(environment).InstructionPath);
    }

    [Fact]
    public void CarriesTheRunLimitWithDefaultsForBothHalves()
    {
        var configuration = Read(Complete());

        // FR-009's ceilings are configuration, not specification: both halves have a default
        // and both are settable.
        Assert.True(configuration.RunMaxToolCalls > 0);
        Assert.True(configuration.RunMaxElapsedMs > 0);
    }

    [Fact]
    public void TakesBothHalvesOfTheRunLimitFromTheEnvironment()
    {
        var environment = Complete();
        environment["GRIMOIRE_RUN_MAX_TOOL_CALLS"] = "12";
        environment["GRIMOIRE_RUN_MAX_ELAPSED_MS"] = "34000";

        var configuration = Read(environment);

        Assert.Equal(12, configuration.RunMaxToolCalls);
        Assert.Equal(34_000, configuration.RunMaxElapsedMs);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public void RefusesARunLimitThatIsNotAPositiveNumber(string value)
    {
        var environment = Complete();
        environment["GRIMOIRE_RUN_MAX_TOOL_CALLS"] = value;

        var failure = Assert.Throws<ConfigurationException>(() => Read(environment));

        Assert.Contains("GRIMOIRE_RUN_MAX_TOOL_CALLS", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesTheFetchProxyWhenSetAndNoneWhenNot()
    {
        Assert.Null(Read(Complete()).FetchProxy);

        var environment = Complete();
        environment["GRIMOIRE_FETCH_PROXY"] = "http://egress:8080/fetch";

        Assert.Equal("http://egress:8080/fetch", Read(environment).FetchProxy);
    }

    [Fact]
    public void LogsStructuredJsonUnlessTextIsAskedFor()
    {
        // JSON is the transport for every declared signal; reading the log by eye is the opt-in.
        Assert.Equal(LogFormat.Json, Read(Complete()).LogFormat);

        var environment = Complete();
        environment["GRIMOIRE_LOG_FORMAT"] = "text";

        Assert.Equal(LogFormat.Text, Read(environment).LogFormat);
    }

    [Fact]
    public void RefusesALogFormatItDoesNotKnow()
    {
        var environment = Complete();
        environment["GRIMOIRE_LOG_FORMAT"] = "pretty";

        var failure = Assert.Throws<ConfigurationException>(() => Read(environment));

        Assert.Contains("GRIMOIRE_LOG_FORMAT", failure.Message, StringComparison.Ordinal);
    }
}

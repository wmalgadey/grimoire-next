using Grimoire.Tests.Support;

namespace Grimoire.Tests.Hub;

/// <summary>
/// Configuration is environment variables and nothing else (contracts/deployment.md "Environment
/// contract"), checked on the hub as a real process — the only place a command line exists.
/// </summary>
/// <remarks>
/// The host's own configuration would also take command-line arguments and settings files. A
/// variable given there must not configure the hub: it would put a token in argv, where every
/// process listing shows it, and make the environment contract untrue.
/// </remarks>
public sealed class ConfigurationSourceTests
{
    [Fact]
    public async Task IgnoresAHubVariableGivenOnTheCommandLine()
    {
        using var wiki = new WikiRepositoryFixture();

        // Honoured, this would switch the log to plain text; the environment says nothing, so the
        // log stays JSON.
        using var hub = await HubProcess.Start(
            wiki,
            cancellationToken: TestContext.Current.CancellationToken,
            arguments: ["--GRIMOIRE_LOG_FORMAT=text"]);
        await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        var lines = hub.Stdout.Where(line => line.Trim().Length > 0).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.StartsWith("{", line.TrimStart(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsToStartAndNamesTheVariableWhenOneIsMissing()
    {
        using var wiki = new WikiRepositoryFixture();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => HubProcess.Start(
            wiki,
            extraEnvironment: new Dictionary<string, string?> { ["GRIMOIRE_MODEL_TOKEN"] = "" },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("exited during startup", failure.Message, StringComparison.Ordinal);
        Assert.Contains("GRIMOIRE_MODEL_TOKEN is required", failure.Message, StringComparison.Ordinal);
    }
}

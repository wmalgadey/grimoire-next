using Grimoire.Dispatch.Adapters;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// The runner's environment is exactly the table in contracts/deployment.md "Runner" — replaced,
/// not merged, so nothing of the hub's own environment reaches the agent (ADR-0007, ADR-0010).
/// </summary>
public sealed class RunnerEnvironmentTests : IDisposable
{
    private readonly string _nodeDirectory =
        Path.Combine(Path.GetTempPath(), $"grimoire-node-{Guid.NewGuid():N}");

    public RunnerEnvironmentTests()
    {
        Directory.CreateDirectory(_nodeDirectory);
        File.WriteAllText(Path.Combine(_nodeDirectory, "node"), "");
    }

    public void Dispose() => Directory.Delete(_nodeDirectory, recursive: true);

    [Fact]
    public void CarriesExactlyTheVariablesTheContractNames()
    {
        var environment = RunnerEnvironment.ForRun(
            "run-1", "http://egress:8080", "an-opaque-internal-token", "src/instructions/ingest.md",
            "/tmp/run-home", "/app");

        Assert.Equal(
            [
                "ANTHROPIC_AUTH_TOKEN",
                "ANTHROPIC_BASE_URL",
                "ANTHROPIC_CUSTOM_HEADERS",
                "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC",
                "GRIMOIRE_INSTRUCTION",
                "HOME",
                "PATH",
            ],
            environment.Variables.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("/app/src/instructions/ingest.md", environment.Variables["GRIMOIRE_INSTRUCTION"]);
        Assert.Equal("X-Grimoire-Run: run-1", environment.Variables["ANTHROPIC_CUSTOM_HEADERS"]);
    }

    [Fact]
    public void GivesTheRunnerAMinimalPathRatherThanTheHubs()
    {
        var hubPath = string.Join(
            Path.PathSeparator, "/opt/hub-only-tools", _nodeDirectory, "/home/someone/.local/bin", "/usr/bin");

        var path = RunnerEnvironment.MinimalPath(hubPath);

        Assert.Equal(string.Join(Path.PathSeparator, _nodeDirectory, "/usr/bin", "/bin"), path);
        Assert.DoesNotContain("hub-only-tools", path, StringComparison.Ordinal);
    }
}

using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// Each run gets a home directory of its own, and it goes with the run (contracts/deployment.md
/// "Runner"). Whatever the SDK keeps there — cached state, anything credential-shaped — must not
/// outlive the run on disk or reach the next one, whichever way the run ended.
/// </summary>
public sealed class RunHomeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovesTheRunsHomeDirectoryHoweverTheRunEnded(bool crashes)
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("no-op");
        using var runner = StubRunner.LeavesStateInItsHome(crashes);
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: runner.Environment);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal(crashes ? "failed" : "completed", task.GetProperty("state").GetString());
        var home = runner.ReportedHome;
        Assert.False(string.IsNullOrEmpty(home), "The runner never reported its home directory.");
        Assert.False(Directory.Exists(home), $"The run's home directory {home} outlived the run.");
    }
}

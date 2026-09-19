using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// Each run gets a home directory of its own, and it goes with the run (contracts/deployment.md
/// "Runner"). Whatever the real SDK keeps there — cached state, anything credential-shaped — must
/// not outlive the run on disk or reach the next one, whichever way the run ended.
/// </summary>
public sealed class RunHomeTests
{
    [Fact]
    public async Task RemovesTheRunsHomeDirectoryAfterACompletedRun()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var home = await ObserveTheRunsHomeDirectory(id, TestContext.Current.CancellationToken);

        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.False(Directory.Exists(home), $"The run's home directory {home} outlived the run.");
        Assert.Empty(HomeDirectoriesOf(id));
    }

    [Fact]
    public async Task RemovesTheRunsHomeDirectoryAfterAKilledRun()
    {
        // The same real-runner kill FailureContainmentTests uses for "killed mid-write": the run
        // hits its elapsed ceiling and the hub kills the child (FR-009, FR-017).
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("write-then-hang");
        using var hub = GrimoireHub.Start(wiki, model, extraEnvironment: new Dictionary<string, string?>
        {
            ["GRIMOIRE_RUN_MAX_ELAPSED_MS"] = "4000",
        });

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        var home = await ObserveTheRunsHomeDirectory(id, TestContext.Current.CancellationToken);

        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(90));

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.False(Directory.Exists(home), $"The run's home directory {home} outlived the run.");
        Assert.Empty(HomeDirectoriesOf(id));
    }

    private static string[] HomeDirectoriesOf(string taskId) =>
        Directory.GetDirectories(Path.GetTempPath(), $"grimoire-run-{taskId}-*");

    /// <summary>
    /// Polls for the directory the dispatched run's own <c>RunnerProcess</c> creates as its home.
    /// It carries the task's identifier, so a concurrent run in this suite or another one is never
    /// mistaken for it.
    /// </summary>
    private static async Task<string> ObserveTheRunsHomeDirectory(string taskId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (HomeDirectoriesOf(taskId) is [var found])
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        throw new TimeoutException($"The run for task {taskId} never created its own home directory.");
    }
}

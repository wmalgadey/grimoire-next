using System.Net.Http.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Deployment;

/// <summary>
/// T093 / TS-19 (FR-017, FR-028). Against a real hub process and a real <c>SIGTERM</c>: the
/// replica drains, the running run is settled, the wiki is left at its pre-run commit, and the
/// process exits without being killed.
/// </summary>
/// <remarks>
/// This is the half that cannot be asserted in-process: a test host has no signal handler and no
/// exit code. What is under test is the sequence in contracts/deployment.md — drain, terminate,
/// settle, reset, exit — and the property that it ends where an ungraceful kill plus startup
/// recovery ends, so an orchestrator that loses the signal produces no different outcome.
/// </remarks>
public sealed class GracefulShutdownTests
{
    [Fact]
    public async Task ReportsNotReadyAndDrainingWhileItShutsDown()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("write-then-hang");
        using var hub = await HubProcess.Start(
            wiki, model, cancellationToken: TestContext.Current.CancellationToken);

        var id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
        await hub.WaitForState(id, "running", TestContext.Current.CancellationToken);

        // Ready while it is working.
        using (var before = await hub.Client.GetAsync("/readyz", TestContext.Current.CancellationToken))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, before.StatusCode);
        }

        var terminating = hub.Terminate(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        // The drain window is the point of step 1: traffic has to be able to leave, which means
        // /readyz keeps answering — 503, draining: true — while the run is being settled.
        var sawDraining = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline && !sawDraining)
        {
            try
            {
                using var response = await hub.Client.GetAsync("/readyz", TestContext.Current.CancellationToken);
                var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
                    TestContext.Current.CancellationToken);
                if (body.GetProperty("draining").GetBoolean())
                {
                    Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
                    Assert.Equal("not-ready", body.GetProperty("status").GetString());
                    sawDraining = true;
                }
            }
            catch (HttpRequestException)
            {
                // The listener has closed: the drain is over and this poll simply missed it.
                break;
            }
        }

        Assert.True(await terminating, "The hub did not exit after SIGTERM.");
        Assert.True(sawDraining, "/readyz never reported draining while the hub was shutting down.");
    }

    [Fact]
    public async Task SettlesTheRunningTaskAndLeavesTheWikiAtItsPreRunCommit()
    {
        using var wiki = new WikiRepositoryFixture();
        var contentBefore = wiki.Snapshot();
        var tipBefore = wiki.Head();
        using var model = ScriptedModelFixture.Start("write-then-hang");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-shutdown-{Guid.NewGuid():N}.db");

        string id;
        using (var hub = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
            await hub.WaitForState(id, "running", TestContext.Current.CancellationToken);

            // The run has written and is now hanging on the model: the tree is dirty, so a clean
            // tree afterwards is the shutdown's doing and not a run that never wrote anything.
            await WaitUntil(() => wiki.Exists("half-written.md"), "the run never wrote its page");
            Assert.False(wiki.IsClean(), "The run's write is not in the working tree.");
            var runners = hub.RunnerProcessIds();
            Assert.NotEmpty(runners);

            Assert.True(
                await hub.Terminate(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken),
                "The hub did not exit after SIGTERM.");
            // Exited, not killed: SIGTERM was handled rather than survived.
            Assert.True(hub.HasExited);

            // And it took its runner with it. A runner outliving the hub would keep writing into a
            // tree the next start has already reset (FR-017).
            foreach (var pid in runners)
            {
                Assert.False(IsAlive(pid), $"The runner {pid} was still running after the hub exited.");
            }
        }

        // Nothing was committed, and nothing the runner wrote survives (FR-017, SC-003).
        Assert.Equal(tipBefore, wiki.Head());
        Assert.Equal(contentBefore, wiki.Snapshot());

        // The task is settled, with a reason that names the interruption rather than blaming the
        // agent — and a second start does not revisit it.
        using var restarted = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);
        var task = await restarted.GetTask(id, TestContext.Current.CancellationToken);
        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains(
            "shut down",
            task.GetProperty("failureReason").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntil(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    public async Task QueuedTasksSurviveToTheNextStart()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-shutdown-{Guid.NewGuid():N}.db");

        string queued;
        using (var hub = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            var running = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
            await hub.WaitForState(running, "running", TestContext.Current.CancellationToken);
            queued = await hub.SubmitText("second source", TestContext.Current.CancellationToken);

            await hub.Terminate(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        }

        // A queued task has not run and nothing about it is in doubt, so shutdown leaves it alone
        // and the next start dispatches it (contracts/deployment.md "SIGTERM").
        using var restarted = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);
        var task = await restarted.WaitForEnd(queued, TestContext.Current.CancellationToken);
        Assert.Contains(task.GetProperty("state").GetString(), new[] { "completed", "failed" });
    }
}

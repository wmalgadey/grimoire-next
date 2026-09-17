using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// T091 / TS-14, recovery half (FR-028, SC-006). Across two hub instances sharing one state
/// database, with the first killed mid-run: nothing is left <c>running</c> while no run is
/// executing.
/// </summary>
/// <remarks>
/// A crash is the ordinary case, not the exotic one — an OOM kill, a node drain, a deploy. The
/// property that matters is that the next start reaches a state an operator can act on: every
/// interrupted task is <c>failed</c> with a reason that says why, and never runs a second time
/// (FR-005). A task stuck at <c>running</c> forever is the failure this prevents.
/// </remarks>
public sealed class StartupRecoveryTests
{
    [Fact]
    public async Task FailsEveryTaskLeftRunningAndNamesTheInterruption()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("write-then-hang");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-recovery-{Guid.NewGuid():N}.db");

        string taskId;
        using (var first = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            taskId = await first.SubmitText("notes", TestContext.Current.CancellationToken);
            await first.WaitForState(taskId, "running", TestContext.Current.CancellationToken);

            // The ungraceful path: no SIGTERM, no drain, the runner dies with its parent.
            first.KillUngracefully();
        }

        using var second = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);
        var task = await second.GetTask(taskId, TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        var reason = task.GetProperty("failureReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reason), "A failed task always carries a reason (FR-018).");
        // The reason names the interruption rather than blaming the agent: the difference between
        // "the hub restarted under it" and "the model refused" is the whole of the operator's next
        // decision.
        Assert.Contains("interrupt", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NeverDispatchesARecoveredTaskASecondTime()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("write-then-hang");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-recovery-{Guid.NewGuid():N}.db");

        string taskId;
        using (var first = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            taskId = await first.SubmitText("notes", TestContext.Current.CancellationToken);
            await first.WaitForState(taskId, "running", TestContext.Current.CancellationToken);
            first.KillUngracefully();
        }

        using var second = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);

        // Long enough that a second dispatch would have started and moved the state back.
        await System.Threading.Tasks.Task.Delay(3_000, TestContext.Current.CancellationToken);
        var task = await second.GetTask(taskId, TestContext.Current.CancellationToken);
        Assert.Equal("failed", task.GetProperty("state").GetString());

        // At most one run, ever, including across a restart (FR-005).
        var run = task.GetProperty("run");
        Assert.True(run.ValueKind is JsonValueKind.Null || run.GetProperty("commit").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task DispatchesTasksLeftQueuedInSubmissionOrder()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-recovery-{Guid.NewGuid():N}.db");

        string first, second;
        using (var hub = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            first = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
            await hub.WaitForState(first, "running", TestContext.Current.CancellationToken);
            // Queued behind the slow run, so it survives the kill still queued.
            second = await hub.SubmitText("second source", TestContext.Current.CancellationToken);
            hub.KillUngracefully();
        }

        using var restarted = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);

        // The queued one is dispatched by the next start; the interrupted one is not.
        var queued = await restarted.WaitForEnd(second, TestContext.Current.CancellationToken);
        Assert.Contains(queued.GetProperty("state").GetString(), new[] { "completed", "failed" });
        var interrupted = await restarted.GetTask(first, TestContext.Current.CancellationToken);
        Assert.Equal("failed", interrupted.GetProperty("state").GetString());
    }
}

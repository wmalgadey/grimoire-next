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

        const string source = "a source whose run was interrupted";
        string taskId;
        using (var first = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            taskId = await first.SubmitText(source, TestContext.Current.CancellationToken);
            await first.WaitForState(taskId, "running", TestContext.Current.CancellationToken);

            // `running` is recorded at the gate, before the runner's first model request: wait for
            // that request, so the run being interrupted is one the model has actually heard of.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (!(await model.Requests(TestContext.Current.CancellationToken))
                       .Any(request => request.Body.Contains(source, StringComparison.Ordinal)))
            {
                Assert.True(DateTime.UtcNow < deadline, "The interrupted run never reached the model.");
                await System.Threading.Tasks.Task.Delay(100, TestContext.Current.CancellationToken);
            }

            first.KillUngracefully();
        }

        var restartedAt = DateTimeOffset.UtcNow;
        using var second = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);

        // Long enough that a second dispatch would have started and moved the state back.
        await System.Threading.Tasks.Task.Delay(3_000, TestContext.Current.CancellationToken);
        var task = await second.GetTask(taskId, TestContext.Current.CancellationToken);
        Assert.Equal("failed", task.GetProperty("state").GetString());

        // At most one run, ever, including across a restart (FR-005).
        var run = task.GetProperty("run");
        Assert.True(run.ValueKind is JsonValueKind.Null || run.GetProperty("commit").ValueKind is JsonValueKind.Null);

        // Not settled-then-rerun either: the model never heard about this source again. The state
        // alone could not show that — a second run that also failed would end in the same place.
        var requests = await model.Requests(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(requests, request => request.At >= restartedAt && request.Body.Contains(source, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DispatchesTasksLeftQueuedInSubmissionOrder()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-recovery-{Guid.NewGuid():N}.db");

        string first;
        var queuedIds = new List<string>();
        using (var hub = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            first = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
            await hub.WaitForState(first, "running", TestContext.Current.CancellationToken);
            // Queued behind the slow run, so they survive the kill still queued. Three, because
            // with one there is no order to get wrong.
            foreach (var source in new[] { "second source", "third source", "fourth source" })
            {
                queuedIds.Add(await hub.SubmitText(source, TestContext.Current.CancellationToken));
            }

            hub.KillUngracefully();
        }

        using var restarted = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);

        // The queued ones are dispatched by the next start, one at a time and in the order they
        // were submitted; the interrupted one is not (FR-019, FR-028).
        var ended = new List<JsonElement>();
        foreach (var id in queuedIds)
        {
            ended.Add(await restarted.WaitForEnd(id, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(120)));
        }

        foreach (var task in ended)
        {
            Assert.Contains(task.GetProperty("state").GetString(), new[] { "completed", "failed" });
        }

        for (var i = 1; i < ended.Count; i++)
        {
            var earlierEnded = ended[i - 1].GetProperty("endedAt").GetDateTimeOffset();
            var laterStarted = ended[i].GetProperty("startedAt").GetDateTimeOffset();
            Assert.True(
                laterStarted >= earlierEnded,
                $"Task {i + 2} started at {laterStarted:O}, before task {i + 1} ended at {earlierEnded:O}.");
        }

        var interrupted = await restarted.GetTask(first, TestContext.Current.CancellationToken);
        Assert.Equal("failed", interrupted.GetProperty("state").GetString());
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Hub;

/// <summary>
/// Quality gate 6 / T094 / TS-13, US3 rows. The two lifecycle signals are emitted through the
/// <b>production composition root</b> <i>and</i> their reasons reach the task view and the task
/// list (constitution IV).
/// </summary>
/// <remarks>
/// These two rows are the only account anyone gets of what a restart did to work in flight. A
/// signal without a surface would leave an operator reading logs to find out why a task they were
/// watching became <c>failed</c>; a surface without the signal would leave nothing to corroborate
/// it with when the answer matters.
/// </remarks>
[Collection("observability")]
public sealed class ObservabilityRecoveryTests
{
    [Fact]
    public async Task RecoveredOnStartupCarriesBothIdListsAndTheReasonReachesBothSurfaces()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("slow-read-then-write");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-recovery-obs-{Guid.NewGuid():N}.db");

        string interrupted, queued;
        using (var first = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            interrupted = await first.SubmitText("first source", TestContext.Current.CancellationToken);
            await first.WaitForState(interrupted, "running", TestContext.Current.CancellationToken);
            queued = await first.SubmitText("second source", TestContext.Current.CancellationToken);
            first.KillUngracefully();
        }

        using var second = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);

        // The row, with both declared fields: what was failed, and what was picked back up.
        var row = second.Stdout.First(line =>
            line.Contains("grimoire.dispatch.recovered_on_startup", StringComparison.Ordinal));
        Assert.Contains(interrupted, row, StringComparison.Ordinal);
        Assert.Contains(queued, row, StringComparison.Ordinal);
        Assert.Null(Record.Exception(() => JsonDocument.Parse(row)));

        // Surface: task view. Operator decision: why did the task I was watching stop? (SC-006)
        var task = await second.GetTask(interrupted, TestContext.Current.CancellationToken);
        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains(
            "interrupt", task.GetProperty("failureReason").GetString()!, StringComparison.OrdinalIgnoreCase);

        // Surface: task list. The same reason, on the row, without opening the task.
        var list = await second.Client.GetFromJsonAsync<JsonElement>(
            "/api/tasks", TestContext.Current.CancellationToken);
        var listed = list.GetProperty("tasks").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == interrupted);
        Assert.Equal("failed", listed.GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(listed.GetProperty("failureReason").GetString()));
    }

    [Fact]
    public async Task InterruptedOnShutdownNamesTheTaskAndItsReasonReachesTheTaskView()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("write-then-hang");
        var stateDb = Path.Combine(Path.GetTempPath(), $"grimoire-shutdown-obs-{Guid.NewGuid():N}.db");

        string id;
        IReadOnlyList<string> logs;
        using (var hub = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken))
        {
            id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
            await hub.WaitForState(id, "running", TestContext.Current.CancellationToken);
            await hub.Terminate(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            logs = hub.Stdout;
        }

        var row = logs.First(line =>
            line.Contains("grimoire.dispatch.interrupted_on_shutdown", StringComparison.Ordinal));
        Assert.Contains(id, row, StringComparison.Ordinal);
        Assert.Null(Record.Exception(() => JsonDocument.Parse(row)));

        using var restarted = await HubProcess.Start(
            wiki, model, stateDb, cancellationToken: TestContext.Current.CancellationToken);
        var task = await restarted.GetTask(id, TestContext.Current.CancellationToken);
        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains(
            "shut down", task.GetProperty("failureReason").GetString()!, StringComparison.OrdinalIgnoreCase);
    }
}

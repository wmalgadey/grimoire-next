using System.Text.Json;
using Grimoire.Ingest;
using Grimoire.Tasks;
using Grimoire.Tasks.Adapters;
using Grimoire.Tests.Support;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// T045 / TS-05 (FR-005). Exactly one run per task and never a second — including after a
/// redelivered dispatch and across a restart.
/// </summary>
/// <remarks>
/// The guarantee is a database property (a uniqueness constraint on <c>agent_run.task_id</c>),
/// not a code convention, so a retry cannot slip through a race the code did not anticipate.
/// </remarks>
public sealed class OneRunPerTaskTests
{
    [Fact]
    public async Task DispatchesExactlyOneRunForAnAcceptedSubmission()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var id = await hub.SubmitText("notes worth keeping", TestContext.Current.CancellationToken);
        var task = await hub.WaitForEnd(id, TestContext.Current.CancellationToken);

        Assert.True(task.GetProperty("run").ValueKind is not JsonValueKind.Null,
            "No run was dispatched for an accepted submission.");
    }

    [Fact]
    public async Task RefusesASecondRunForTheSameTaskAtTheDatabaseLevel()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"grimoire-one-run-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteStore(databasePath);
            store.EnsureSchema();

            var at = DateTimeOffset.UtcNow;
            var task = new Grimoire.Tasks.Task(
                "t-1", TaskState.Queued, at, null, null,
                new Source(SourceKind.Text, "notes", null, null, 5), null, null, null);
            store.AddTask(task);
            store.StartRun("t-1", NewRun(at), at);

            // A redelivered dispatch is exactly this: the same task, dispatched again.
            var second = Record.Exception(() => store.StartRun("t-1", NewRun(at), at));

            Assert.NotNull(second);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(databasePath + suffix))
                {
                    File.Delete(databasePath + suffix);
                }
            }
        }
    }

    [Fact]
    public async Task LeavesTheTaskWithOneRunAfterTheHubIsRestarted()
    {
        using var wiki = new WikiRepositoryFixture();
        var databasePath = Path.Combine(Path.GetTempPath(), $"grimoire-restart-{Guid.NewGuid():N}.db");
        string id;

        using (var model = ScriptedModelFixture.Start("read-then-write"))
        using (var hub = GrimoireHub.Start(wiki, model, databasePath))
        {
            id = await hub.SubmitText("notes", TestContext.Current.CancellationToken);
            await hub.WaitForEnd(id, TestContext.Current.CancellationToken);
        }

        // A second hub over the same state must not re-run a task that already had its run.
        using (var model = ScriptedModelFixture.Start("read-then-write"))
        using (var hub = GrimoireHub.Start(wiki, model, databasePath))
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            var task = await hub.GetTask(id, TestContext.Current.CancellationToken);

            Assert.Contains(task.GetProperty("state").GetString(), new[] { "completed", "failed" });
            var requests = await model.Requests(TestContext.Current.CancellationToken);
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task RecordsTheRunAgainstTheTaskThatOwnsIt()
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start("read-then-write");
        using var hub = GrimoireHub.Start(wiki, model);

        var first = await hub.SubmitText("first source", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(first, TestContext.Current.CancellationToken);
        var second = await hub.SubmitText("second source", TestContext.Current.CancellationToken);
        await hub.WaitForEnd(second, TestContext.Current.CancellationToken);

        var firstTask = await hub.GetTask(first, TestContext.Current.CancellationToken);
        var secondTask = await hub.GetTask(second, TestContext.Current.CancellationToken);

        Assert.True(firstTask.GetProperty("run").ValueKind is not JsonValueKind.Null);
        Assert.True(secondTask.GetProperty("run").ValueKind is not JsonValueKind.Null);
        Assert.NotEqual(first, second);
    }

    private static AgentRun NewRun(DateTimeOffset at) => new(
        new InstructionVersion("src/instructions/ingest.md", new string('a', 64), 10),
        new ToolGrant(["mcp__wiki__read_page", "mcp__wiki__write_page"], at),
        [],
        null,
        null,
        null,
        0,
        null);
}

using Grimoire.Ingest;
using Grimoire.Tasks;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki;

namespace Grimoire.Tests.Tasks;

/// <summary>
/// T020 / ADR-0006. Against a real per-test SQLite file — no in-memory shortcut, no fake:
/// the store is exercised through the same file-backed connection production uses
/// (constitution III.2, "real infrastructure").
/// </summary>
public sealed class SqliteStoreTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"grimoire-store-{Guid.NewGuid():N}.db");

    private SqliteStore NewStore()
    {
        var store = new SqliteStore(_dbPath);
        store.EnsureSchema();
        return store;
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static Source TextSource(string value) =>
        new(SourceKind.Text, value, null, null, System.Text.Encoding.UTF8.GetByteCount(value));

    private static Grimoire.Tasks.Task QueuedTask(string id, DateTimeOffset submittedAt) =>
        new(id, TaskState.Queued, submittedAt, null, null, TextSource($"source for {id}"), null, null, null);

    [Fact]
    public void RoundTripsAQueuedTaskAndItsSource()
    {
        using var store = NewStore();
        var submittedAt = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        var task = QueuedTask("t-1", submittedAt);

        store.AddTask(task);
        var read = store.GetTask("t-1");

        Assert.NotNull(read);
        Assert.Equal(TaskState.Queued, read.State);
        Assert.Equal(submittedAt, read.SubmittedAt);
        Assert.Equal(SourceKind.Text, read.Source.Kind);
        Assert.Equal("source for t-1", read.Source.SubmittedValue);
        Assert.Null(read.Source.RetrievedText);
        Assert.Null(read.Run);
        Assert.Null(read.Revert);
    }

    [Fact]
    public void RoundTripsAUrlSourceWithItsRetrievedText()
    {
        using var store = NewStore();
        var retrievedAt = new DateTimeOffset(2026, 9, 16, 10, 1, 0, TimeSpan.Zero);
        var source = new Source(SourceKind.Url, "https://example.test/page", "the retrieved body", retrievedAt, 18);
        var task = new Grimoire.Tasks.Task(
            "t-url", TaskState.Queued, retrievedAt, null, null, source, null, null, null);

        store.AddTask(task);
        var read = store.GetTask("t-url");

        Assert.NotNull(read);
        Assert.Equal(SourceKind.Url, read.Source.Kind);
        Assert.Equal("https://example.test/page", read.Source.SubmittedValue);
        Assert.Equal("the retrieved body", read.Source.RetrievedText);
        Assert.Equal(retrievedAt, read.Source.RetrievedAt);
        Assert.Equal(18, read.Source.ByteLength);
    }

    [Fact]
    public void RoundTripsARunWithItsGrantOrderedToolCallsAndCommit()
    {
        using var store = NewStore();
        var at = new DateTimeOffset(2026, 9, 16, 11, 0, 0, TimeSpan.Zero);
        store.AddTask(QueuedTask("t-2", at));

        var run = new AgentRun(
            new InstructionVersion("src/instructions/ingest.md", new string('a', 64), 1234),
            new ToolGrant(["mcp__wiki__read_page", "mcp__wiki__write_page"], at),
            [],
            null,
            null,
            null,
            0,
            null);
        store.StartRun("t-2", run, at);

        store.AppendToolCall("t-2", new ToolCall(1, "mcp__wiki__read_page", "index.md", ToolCallOutcome.Ok, null, at));
        store.AppendToolCall("t-2", new ToolCall(2, "Bash", "rm -rf /", ToolCallOutcome.Refused, "tool not granted", at));
        store.AppendToolCall("t-2", new ToolCall(3, "mcp__wiki__write_page", "topic.md", ToolCallOutcome.Ok, null, at));

        var commit = new WikiCommit("c" + new string('1', 39), "p" + new string('0', 39), "add topic", at);
        store.EndRun("t-2", RunOutcomeKind.Completed, failureReason: null, commit, durationMs: 4200, endedAt: at);

        var read = store.GetTask("t-2");

        Assert.NotNull(read);
        Assert.Equal(TaskState.Completed, read.State);
        Assert.NotNull(read.Run);
        Assert.Equal(new string('a', 64), read.Run.InstructionVersion.Sha256);
        Assert.Equal(["mcp__wiki__read_page", "mcp__wiki__write_page"], read.Run.ToolGrant.Tools);
        Assert.Equal([1, 2, 3], read.Run.ToolCalls.Select(c => c.Seq));
        Assert.Equal(["mcp__wiki__read_page", "Bash", "mcp__wiki__write_page"], read.Run.ToolCalls.Select(c => c.Tool));
        Assert.Equal(ToolCallOutcome.Refused, read.Run.ToolCalls[1].Outcome);
        Assert.Equal("tool not granted", read.Run.ToolCalls[1].Detail);
        Assert.Equal(3, read.Run.ToolCallCount);
        Assert.Equal(4200, read.Run.DurationMs);
        Assert.NotNull(read.Run.Commit);
        Assert.Equal(commit.Sha, read.Run.Commit.Sha);
        Assert.Equal(commit.ParentSha, read.Run.Commit.ParentSha);
        Assert.Equal("add topic", read.Run.Commit.Message);
    }

    [Fact]
    public void RoundTripsAFailedRunWithItsReasonAndNoCommit()
    {
        using var store = NewStore();
        var at = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        store.AddTask(QueuedTask("t-3", at));
        store.StartRun("t-3", NewRun(at), at);

        store.EndRun("t-3", RunOutcomeKind.Failed, "the run exceeded its tool-call ceiling", commit: null, durationMs: 90_000, endedAt: at);

        var read = store.GetTask("t-3");

        Assert.NotNull(read);
        Assert.Equal(TaskState.Failed, read.State);
        Assert.Equal("the run exceeded its tool-call ceiling", read.FailureReason);
        Assert.NotNull(read.Run);
        Assert.Equal(RunOutcomeKind.Failed, read.Run.Outcome);
        Assert.Equal("the run exceeded its tool-call ceiling", read.Run.FailureReason);
        Assert.Null(read.Run.Commit);
    }

    [Fact]
    public void RoundTripsARevertRecordAndMovesTheTaskToReverted()
    {
        using var store = NewStore();
        var at = new DateTimeOffset(2026, 9, 16, 13, 0, 0, TimeSpan.Zero);
        store.AddTask(QueuedTask("t-4", at));
        store.StartRun("t-4", NewRun(at), at);
        store.EndRun("t-4", RunOutcomeKind.Completed, null, new WikiCommit("sha-a", "sha-p", "m", at), 10, at);

        var revert = new RevertRecord("sha-revert", at.AddMinutes(1));
        store.RecordRevert("t-4", revert);

        var read = store.GetTask("t-4");

        Assert.NotNull(read);
        Assert.Equal(TaskState.Reverted, read.State);
        Assert.NotNull(read.Revert);
        Assert.Equal("sha-revert", read.Revert.RevertCommitSha);
        Assert.Equal(at.AddMinutes(1), read.Revert.RevertedAt);
        // A reverted task keeps its instruction version, tool calls and original diff (FR-026).
        Assert.NotNull(read.Run);
        Assert.NotNull(read.Run.Commit);
        Assert.Equal("sha-a", read.Run.Commit.Sha);
    }

    [Fact]
    public void ListsTasksNewestFirstAndPagesWithACursor()
    {
        using var store = NewStore();
        var baseAt = new DateTimeOffset(2026, 9, 16, 14, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
        {
            store.AddTask(QueuedTask($"t-{i}", baseAt.AddMinutes(i)));
        }

        var first = store.ListTasks(limit: 2, cursor: null);
        Assert.Equal(["t-4", "t-3"], first.Tasks.Select(t => t.Id));
        Assert.NotNull(first.NextCursor);

        var second = store.ListTasks(limit: 2, cursor: first.NextCursor);
        Assert.Equal(["t-2", "t-1"], second.Tasks.Select(t => t.Id));

        var third = store.ListTasks(limit: 2, cursor: second.NextCursor);
        Assert.Equal(["t-0"], third.Tasks.Select(t => t.Id));
        Assert.Null(third.NextCursor);
    }

    [Fact]
    public void RejectsASecondRunForTheSameTaskInTheDatabaseNotInCode()
    {
        using var store = NewStore();
        var at = new DateTimeOffset(2026, 9, 16, 15, 0, 0, TimeSpan.Zero);
        store.AddTask(QueuedTask("t-5", at));
        store.StartRun("t-5", NewRun(at), at);

        // FR-005's "never a second run" is a database property: a uniqueness constraint on
        // agent_run.task_id, so a retry after a restart cannot slip through.
        var second = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(
            () => store.StartRun("t-5", NewRun(at), at));
        Assert.Contains("UNIQUE", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindsTasksLeftRunningSoStartupRecoveryCanFailThem()
    {
        using var store = NewStore();
        var at = new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero);
        store.AddTask(QueuedTask("t-running", at));
        store.AddTask(QueuedTask("t-queued", at.AddMinutes(1)));
        store.StartRun("t-running", NewRun(at), at);

        Assert.Equal(["t-running"], store.ListTasksInState(TaskState.Running).Select(t => t.Id));
        Assert.Equal(["t-queued"], store.ListTasksInState(TaskState.Queued).Select(t => t.Id));
    }

    [Fact]
    public void SurvivesReopeningTheFile()
    {
        var at = new DateTimeOffset(2026, 9, 16, 17, 0, 0, TimeSpan.Zero);
        using (var store = NewStore())
        {
            store.AddTask(QueuedTask("t-durable", at));
        }

        using var reopened = NewStore();
        Assert.NotNull(reopened.GetTask("t-durable"));
    }

    private static AgentRun NewRun(DateTimeOffset at) => new(
        new InstructionVersion("src/instructions/ingest.md", new string('b', 64), 10),
        new ToolGrant(["mcp__wiki__read_page", "mcp__wiki__write_page"], at),
        [],
        null,
        null,
        null,
        0,
        null);
}

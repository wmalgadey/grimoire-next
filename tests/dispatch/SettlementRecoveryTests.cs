using System.Text.Json;
using Grimoire.Ingest;
using Grimoire.Tasks;
using Grimoire.Tasks.Adapters;
using Grimoire.Tests.Support;
using Grimoire.Wiki.Adapters;

namespace Grimoire.Tests.Dispatch;

/// <summary>
/// The window between a git commit and the task that records it (FR-015, FR-025, FR-028). A hub
/// that dies after moving the branch but before the store says so leaves a commit no task accounts
/// for. Git history is the only journal a commit is ever written to: startup reads HEAD and, when
/// it finds a commit no task recorded, attributes it to whichever task it plainly belongs to
/// (<c>StartupRecovery.ReconcileHead</c>) — rather than keep a second, provisional record of it.
/// </summary>
/// <remarks>
/// Each test builds the exact state the crash leaves — a real store file, a real repository, the
/// commit made by the same single-step plumbing the hub uses (<c>GitCli.CommitAll</c> /
/// <c>GitCli.Revert</c>) — and then boots the hub on it through the production composition root.
/// Killing a process at the right instruction would test luck.
/// </remarks>
public sealed class SettlementRecoveryTests : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), $"grimoire-settlement-{Guid.NewGuid():N}.db");
    private readonly WikiRepositoryFixture _wiki = new();

    public void Dispose()
    {
        _wiki.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_state + suffix);
        }
    }

    [Fact]
    public async Task AdoptsARunCommitThatLandedBeforeTheHubCouldRecordIt()
    {
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        using (var store = OpenStore())
        {
            AddRunningTask(store, "t-landed", startedAt);

            // The run wrote and committed — the same single git commit the hub itself would make
            // — and the process died before EndRun.
            _wiki.Write("topics/landed.md", "# Landed\n");
            new GitCli(_wiki.Path).CommitAll("Add landed topic");
        }

        var landed = _wiki.Head();
        using var hub = GrimoireHub.Start(_wiki, stateDatabasePath: _state);
        var task = await hub.GetTask("t-landed", TestContext.Current.CancellationToken);

        // Completed with the commit it made: the run's work is history, and now the task says so.
        Assert.Equal("completed", task.GetProperty("state").GetString());
        var recorded = task.GetProperty("run").GetProperty("commit");
        Assert.Equal(landed, recorded.GetProperty("sha").GetString());
        Assert.Equal("Add landed topic", recorded.GetProperty("message").GetString());
        Assert.True(task.GetProperty("revertEligibility").GetProperty("eligible").GetBoolean());

        // Nothing rewritten, nothing added: history is exactly what the run made.
        Assert.Equal(landed, _wiki.Head());
        Assert.Equal(2, _wiki.CommitCount());
    }

    [Fact]
    public async Task FailsTheTaskAsInterruptedWhenNoCommitWasEverMade()
    {
        // The run crashed before writing or committing anything. HEAD is still the repository's
        // own root commit — parentless, and therefore never a candidate for attribution: nothing
        // reconciles it, and the ordinary running→failed pass is what fails this task.
        var tip = _wiki.Head();
        using (var store = OpenStore())
        {
            AddRunningTask(store, "t-unlanded", DateTimeOffset.UtcNow.AddSeconds(-5));
        }

        using var hub = GrimoireHub.Start(_wiki, stateDatabasePath: _state);
        var task = await hub.GetTask("t-unlanded", TestContext.Current.CancellationToken);

        Assert.Equal("failed", task.GetProperty("state").GetString());
        Assert.Contains("interrupted", task.GetProperty("failureReason").GetString()!, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, task.GetProperty("run").GetProperty("commit").ValueKind);
        Assert.Equal(tip, _wiki.Head());
    }

    [Fact]
    public async Task AdoptsARevertCommitThatLandedBeforeTheHubCouldRecordIt()
    {
        var git = new GitCli(_wiki.Path);
        using (var store = OpenStore())
        {
            var startedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
            AddRunningTask(store, "t-reverted", startedAt);
            _wiki.Write("topics/kept.md", "# Kept\n");
            var commit = git.CommitAll("Add kept topic")!;
            store.EndRun("t-reverted", RunOutcomeKind.Completed, null, commit, 1_000, startedAt.AddSeconds(1));

            // The revert landed as the same single commit git.Revert always makes, and the process
            // died before RecordRevert.
            git.Revert(commit.Sha);
        }

        var reverted = _wiki.Head();
        using var hub = GrimoireHub.Start(_wiki, stateDatabasePath: _state);
        var task = await hub.GetTask("t-reverted", TestContext.Current.CancellationToken);

        // Reverted with the revert that happened, rather than completed with its commit now
        // superseded — which would offer an undo that already took place.
        Assert.Equal("reverted", task.GetProperty("state").GetString());
        Assert.Equal(reverted, task.GetProperty("revert").GetProperty("revertCommitSha").GetString());
        Assert.False(_wiki.Exists("topics/kept.md"));
        Assert.Equal(3, _wiki.CommitCount());
    }

    [Fact]
    public async Task LeavesTheTaskCompletedWhenNoRevertWasEverMade()
    {
        var git = new GitCli(_wiki.Path);
        string kept;
        using (var store = OpenStore())
        {
            var startedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
            AddRunningTask(store, "t-kept", startedAt);
            _wiki.Write("topics/kept.md", "# Kept\n");
            var commit = git.CommitAll("Add kept topic")!;
            kept = commit.Sha;
            store.EndRun("t-kept", RunOutcomeKind.Completed, null, commit, 1_000, startedAt.AddSeconds(1));

            // No revert was ever attempted: HEAD is still this task's own commit, already recorded.
        }

        using var hub = GrimoireHub.Start(_wiki, stateDatabasePath: _state);
        var task = await hub.GetTask("t-kept", TestContext.Current.CancellationToken);

        // Nothing to reconcile: the task is as it was, and can still be reverted.
        Assert.Equal("completed", task.GetProperty("state").GetString());
        Assert.True(task.GetProperty("revertEligibility").GetProperty("eligible").GetBoolean());
        Assert.Equal(kept, _wiki.Head());
        Assert.True(_wiki.Exists("topics/kept.md"));
    }

    private SqliteStore OpenStore()
    {
        var store = new SqliteStore(_state);
        store.EnsureSchema();
        return store;
    }

    private static void AddRunningTask(SqliteStore store, string id, DateTimeOffset startedAt)
    {
        var source = new Source(SourceKind.Text, "notes", null, null, 5);
        store.AddTask(new Grimoire.Tasks.Task(id, TaskState.Queued, startedAt.AddSeconds(-1), null, null, source, null, null, null));
        store.StartRun(
            id,
            new AgentRun(
                new InstructionVersion("src/instructions/ingest.md", new string('a', 64), 10),
                new ToolGrant(["mcp__wiki__read_page", "mcp__wiki__write_page"], startedAt),
                [],
                null,
                null,
                null,
                0,
                null),
            startedAt);
    }
}

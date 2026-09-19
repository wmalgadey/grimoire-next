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
            new GitCli(_wiki.Path).CommitAll("Add landed topic\n\nFrom the notes, whole.");
        }

        var landed = _wiki.Head();
        using var hub = GrimoireHub.Start(_wiki, stateDatabasePath: _state);
        var task = await hub.GetTask("t-landed", TestContext.Current.CancellationToken);

        // Completed with the commit it made: the run's work is history, and now the task says so.
        Assert.Equal("completed", task.GetProperty("state").GetString());
        var recorded = task.GetProperty("run").GetProperty("commit");
        Assert.Equal(landed, recorded.GetProperty("sha").GetString());
        // The message whole, body and all, as the hub itself would have recorded it.
        Assert.Equal("Add landed topic\n\nFrom the notes, whole.", recorded.GetProperty("message").GetString());
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
    public async Task AdoptsARunCommitWhoseMessageReadsLikeARevertAsTheRunsOwn()
    {
        // A run's commit message is the model's final message, verbatim, and nothing stops it
        // beginning "Revert". Landing on top of an earlier task's recorded commit, that is the
        // shape a revert has — except that the hub did not make it.
        var git = new GitCli(_wiki.Path);
        string earlier;
        using (var store = OpenStore())
        {
            var startedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
            AddRunningTask(store, "t-earlier", startedAt);
            _wiki.Write("topics/claim.md", "# Claim\n");
            var commit = git.CommitAll("Add claim")!;
            earlier = commit.Sha;
            store.EndRun("t-earlier", RunOutcomeKind.Completed, null, commit, 1_000, startedAt.AddSeconds(1));

            AddRunningTask(store, "t-later", startedAt.AddSeconds(5));
            _wiki.Write("topics/claim.md", "# Claim, corrected\n");
            git.CommitAll("Revert the claim the last source made");
        }

        var landed = _wiki.Head();
        using var hub = GrimoireHub.Start(_wiki, stateDatabasePath: _state);
        var later = await hub.GetTask("t-later", TestContext.Current.CancellationToken);
        var prior = await hub.GetTask("t-earlier", TestContext.Current.CancellationToken);

        // The commit is the running task's, completed; the earlier task was never reverted.
        Assert.Equal("completed", later.GetProperty("state").GetString());
        Assert.Equal(landed, later.GetProperty("run").GetProperty("commit").GetProperty("sha").GetString());
        Assert.Equal("completed", prior.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, prior.GetProperty("revert").ValueKind);
        Assert.Equal(earlier, prior.GetProperty("run").GetProperty("commit").GetProperty("sha").GetString());
    }

    [Fact]
    public async Task ClaimsNoRevertItCouldNotRecord()
    {
        // A revert commit of a task's run commit, when that task already shows a revert of its own:
        // it is not completed, so the revert cannot be put on it, and recovery must not say it was.
        var git = new GitCli(_wiki.Path);
        var earlierRevert = new string('e', 40);
        using (var store = OpenStore())
        {
            var startedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
            AddRunningTask(store, "t-twice", startedAt);
            _wiki.Write("topics/kept.md", "# Kept\n");
            var commit = git.CommitAll("Add kept topic")!;
            store.EndRun("t-twice", RunOutcomeKind.Completed, null, commit, 1_000, startedAt.AddSeconds(1));
            store.RecordRevert("t-twice", new RevertRecord(earlierRevert, startedAt.AddSeconds(2)));
            git.Revert(commit.Sha);
        }

        var head = _wiki.Head();
        using var hub = await HubProcess.Start(
            _wiki, stateDatabasePath: _state, cancellationToken: TestContext.Current.CancellationToken);
        var task = await hub.GetTask("t-twice", TestContext.Current.CancellationToken);

        // The task keeps the revert it had; the unaccounted one is handed to an operator.
        Assert.Equal("reverted", task.GetProperty("state").GetString());
        Assert.Equal(earlierRevert, task.GetProperty("revert").GetProperty("revertCommitSha").GetString());

        // Let the logger flush the last line before reading it.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var logs = hub.Stdout;
        Assert.DoesNotContain(logs, line =>
            line.Contains("grimoire.wiki.reverted", StringComparison.Ordinal)
            && line.Contains(head, StringComparison.Ordinal));
        Assert.Contains(logs, line =>
            line.Contains("reconcile it with the task store by hand", StringComparison.Ordinal)
            && line.Contains(head, StringComparison.Ordinal));
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

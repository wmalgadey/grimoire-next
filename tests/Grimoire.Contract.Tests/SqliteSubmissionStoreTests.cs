using Grimoire.Agent;
using Grimoire.Runs;
using Grimoire.Runs.Adapters;
using Microsoft.Data.Sqlite;

namespace Grimoire.Contract.Tests;

/// <summary>
/// The submission store against a real SQLite file: what was written through one connection is
/// what a later one reads back (RUNS-004).
/// </summary>
/// <remarks>
/// <para>
/// One suite per adapter against the real external thing (Constitution III.4). The Fast suite has
/// an in-memory adapter at the same port, and an in-memory adapter cannot show that a change
/// reached a file at all — which is the whole of what RUNS-004 rests on.
/// </para>
/// <para>
/// What is <b>not</b> proven here: that a committed SQLite transaction survives the process being
/// killed. That is SQLite's decision and its own test suite's business, and we test the decisions
/// we made (III.8, research.md R-02). The one place a real uncatchable stop is exercised is the
/// owner's acceptance run, where the hub is killed with <c>kill -9</c> and started again
/// (quickstart.md). No sign-in is needed here, so this runs in CI.
/// </para>
/// </remarks>
[Trait("level", "contract")]
public sealed class SqliteSubmissionStoreTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("grimoire-store-").FullName;

    /// <summary>
    /// A store over the same file, built afresh. Every read in this suite goes through one of
    /// these, because a store that answered from what it still held in memory would prove nothing
    /// about the file.
    /// </summary>
    private SqliteSubmissionStore Reopened() => new(directory);

    private static StoredSubmission ASubmission(string text, DateTimeOffset submittedAt) =>
        new(Guid.NewGuid(), text, submittedAt, SubmissionState.Submitted, Run: null, AcknowledgedAt: null);

    private static StoredRun ARun(Guid submissionId, DateTimeOffset startedAt) =>
        new(Guid.NewGuid(), submissionId, startedAt, ToolGrant.ForIngest, startedAt, PinnedModel, AgentProcess: null);

    /// <summary>
    /// A run with <b>no submission behind it</b> — one a question caused. Its grant is the read-only one,
    /// because that is what such a run is served (GUARD-005).
    /// </summary>
    private static StoredRun AQuestionsRun() => new(
        Guid.NewGuid(),
        QueuedId: null,
        Noon,
        ToolGrant.ForQuestion,
        Noon,
        PinnedModel,
        AgentProcess: null);

    /// <summary>The model a run is recorded against (DEC-010).</summary>
    private const string PinnedModel = "claude-opus-4-5-20251101";

    /// <summary>
    /// The four raw counts a run caused, all different from one another so that a column written
    /// into the wrong one of the four would be read back wrong (GUARD-004, RUNS-010).
    /// </summary>
    private static readonly ModelTokens Spent = new(120_000, 4_000, 60_000, 3_000);

    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("req", "RUNS-004")]
    public void Added_IsReadBack_WithItsTextItsTimeAndItsState()
    {
        var submission = ASubmission("Ada Lovelace wrote the first program.", Noon);

        Reopened().Add(submission);

        var held = Assert.Single(Reopened().Load());
        Assert.Equal(submission.Id, held.Id);
        Assert.Equal("Ada Lovelace wrote the first program.", held.Text);
        Assert.Equal(Noon, held.SubmittedAt);
        Assert.Equal(SubmissionState.Submitted, held.State);
        Assert.Null(held.Run);
        Assert.Null(held.AcknowledgedAt);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public void Added_IsReadBack_WithTheTextWholeAndUntidied()
    {
        // Every run receives what the user pasted (INGEST-002), so the file has to keep it that
        // way: the newlines, the quotes and the apostrophes among them.
        const string Pasted = "  # A heading\n\n  \"Quoted\", and O'Brien's — ünïcödé …\n\n";
        Reopened().Add(ASubmission(Pasted, Noon));

        Assert.Equal(Pasted, Assert.Single(Reopened().Load()).Text);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "GUARD-003")]
    public void Run_IsReadBack_WithItsIdentifierAndItsGrantedTools()
    {
        var submission = ASubmission("A text.", Noon);
        var run = ARun(submission.Id, Noon.AddSeconds(2));

        var store = Reopened();
        store.Add(submission);
        store.AssignRun(submission.Id, run);

        var held = Assert.Single(Reopened().Load()).Run;
        Assert.NotNull(held);
        Assert.Equal(run.Id, held!.Id);
        Assert.Equal(submission.Id, held.QueuedId);
        Assert.Equal(Noon.AddSeconds(2), held.StartedAt);

        // The grant is recorded for every run (GUARD-003), and once the submission outlives the
        // process the grant has to outlive it too (research.md R-08).
        Assert.Equal(ToolGrant.ForIngest, held.GrantedTools);
        Assert.Equal(Noon.AddSeconds(2), held.GrantRecordedAt);
        Assert.Null(held.AgentProcess);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public void AgentProcess_IsReadBack_AsBothHalvesOfTheIdentity()
    {
        var submission = ASubmission("A text.", Noon);
        var run = ARun(submission.Id, Noon);
        var identity = new AgentProcessIdentity(31_337, Noon.AddSeconds(1));

        var store = Reopened();
        store.Add(submission);
        store.AssignRun(submission.Id, run);
        store.RecordAgentProcess(run.Id, identity);

        // The pair, never the number alone: it is what the next start-up recognises the agent by,
        // and what stops it ending an unrelated program (RUNS-006, research.md R-11).
        Assert.Equal(identity, Assert.Single(Reopened().Load()).Run!.AgentProcess);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public void State_IsReadBack_AsItWasLastWritten()
    {
        var submission = ASubmission("A text.", Noon);
        var store = Reopened();
        store.Add(submission);

        store.SetState(submission.Id, SubmissionState.Running);
        Assert.Equal(SubmissionState.Running, Assert.Single(Reopened().Load()).State);

        Reopened().SetState(submission.Id, SubmissionState.Failed);
        Assert.Equal(SubmissionState.Failed, Assert.Single(Reopened().Load()).State);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public void Acknowledgement_IsReadBack()
    {
        var submission = ASubmission("A text.", Noon);
        var store = Reopened();
        store.Add(submission);
        store.SetState(submission.Id, SubmissionState.Failed);

        store.Acknowledge(submission.Id, Noon.AddMinutes(5));

        // A restart does not re-block a queue the user has already cleared (RUNS-003).
        Assert.Equal(Noon.AddMinutes(5), Assert.Single(Reopened().Load()).AcknowledgedAt);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-002")]
    public void Load_ReturnsSubmissions_OldestFirst()
    {
        var store = Reopened();
        var first = ASubmission("The first text.", Noon);
        var second = ASubmission("The second text.", Noon.AddMinutes(1));
        var third = ASubmission("The third text.", Noon.AddMinutes(2));

        store.Add(first);
        store.Add(second);
        store.Add(third);

        // The order they were accepted in, which is the order the queue takes them in (RUNS-002).
        // They are added in that order because that is the only order the board ever adds them in.
        Assert.Equal(
            [first.Id, second.Id, third.Id],
            Reopened().Load().Select(s => s.Id));
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-002")]
    public void Load_ReturnsSubmissions_InTheOrderTheyWereAccepted_AfterTheClockWasPutBack()
    {
        var store = Reopened();
        var first = ASubmission("The first text.", Noon);

        // The machine's clock was corrected backwards between the two, so the second submission
        // carries the earlier stamp. The order they were made in has not changed, and the queue
        // this Grimoire rebuilds must be the queue the last one had (RUNS-002, RUNS-004).
        var second = ASubmission("The second text.", Noon.AddMinutes(-10));

        store.Add(first);
        store.Add(second);

        Assert.Equal([first.Id, second.Id], Reopened().Load().Select(s => s.Id));
    }

    [Fact]
    public void Load_ReturnsNothing_FromAFileThatDidNotExist()
    {
        // The first start of a new Grimoire: the file and its two tables are made here, and there
        // is nothing to read back.
        Assert.Empty(Reopened().Load());
    }

    [Fact]
    [Trait("req", "RUNS-010")]
    public void Figures_RoundTripThroughTheFile()
    {
        var submission = ASubmission("Ada Lovelace wrote the first program.", Noon);
        var run = ARun(submission.Id, Noon);
        var store = Reopened();

        store.Add(submission);
        store.AssignRun(submission.Id, run);
        store.RecordFigures(run.Id, costSpent: 148_233, Spent, toolCalls: 9, entriesLost: 2);

        var read = Reopened().Load().Single().Run!;

        Assert.Equal(PinnedModel, read.Model);
        Assert.Equal(148_233, read.CostSpent);

        // The four raw counts come back beside the figure they were weighed into. Kept because the
        // weighting cannot be undone and the cost ceiling is calibrated from what runs actually
        // caused, which a restart must not lose either (GUARD-004, RUNS-010).
        Assert.Equal(Spent, read.Tokens);
        Assert.Equal(9, read.ToolCalls);
        Assert.Equal(2, read.EntriesLost);
    }

    [Fact]
    [Trait("req", "RUNS-010")]
    [Trait("req", "RUNS-004")]
    public void Ending_PutsTheTerminalStateAndTheFinalFiguresInTheFileTogether()
    {
        var submission = ASubmission("Ada Lovelace wrote the first program.", Noon);
        var run = ARun(submission.Id, Noon);
        var store = Reopened();

        store.Add(submission);
        store.AssignRun(submission.Id, run);
        store.RecordFigures(run.Id, costSpent: 51_094, default, toolCalls: 4, entriesLost: 0);

        store.Ended(
            submission.Id, SubmissionState.Failed, run.Id, costSpent: 148_233, Spent, toolCalls: 9, entriesLost: 2);

        // One change, so a stop can leave the file before it or after it and never between: a
        // submission reading failed beside the figures it had one moment earlier is what RUNS-010
        // forbids, and two writes could not promise otherwise.
        var read = Reopened().Load().Single();

        Assert.Equal(SubmissionState.Failed, read.State);
        Assert.Equal(148_233, read.Run!.CostSpent);
        Assert.Equal(Spent, read.Run.Tokens);
        Assert.Equal(9, read.Run.ToolCalls);
        Assert.Equal(2, read.Run.EntriesLost);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    [Trait("req", "RUNS-010")]
    [Trait("req", "GUARD-003")]
    public void RunWithoutASubmission_RoundTripsThroughTheFile()
    {
        var store = Reopened();
        var run = AQuestionsRun();

        store.AddRun(run);
        store.RecordAgentProcess(run.Id, new AgentProcessIdentity(4_711, Noon));
        store.RecordFigures(run.Id, costSpent: 148_233, Spent, toolCalls: 3, entriesLost: 0);

        // Read back through a store built afresh over the same file, because a store answering from
        // what it still held in memory would prove nothing about the file. Only the real SQLite decides
        // whether a null `submission_id` round-trips at all (Constitution III.4).
        var read = Assert.Single(Reopened().LoadRunsWithoutASubmission());

        Assert.Equal(run.Id, read.Id);

        // The null is the point: nothing of the question is on disk, so there is nothing for this to
        // point at (QUERY-005, research.md R-04).
        Assert.Null(read.QueuedId);

        Assert.Equal(Noon, read.StartedAt);
        Assert.Equal(ToolGrant.ForQuestion, read.GrantedTools);
        Assert.Equal(PinnedModel, read.Model);
        Assert.Equal(new AgentProcessIdentity(4_711, Noon), read.AgentProcess);
        Assert.Equal(148_233, read.CostSpent);
        Assert.Equal(Spent, read.Tokens);
        Assert.Equal(3, read.ToolCalls);
    }

    [Fact]
    [Trait("req", "RUNS-006")]
    public void RunWithoutASubmission_IsReadBackAsInProgress_UntilItHasEnded()
    {
        var store = Reopened();
        var run = AQuestionsRun();

        store.AddRun(run);

        // In progress, which is what a start-up reads these back for: it terminates the agent of every
        // run it finds in that state before anything else runs, and a run with nothing on disk would
        // leave an orphaned `claude` holding the granted tools with no ceiling on it (RUNS-006).
        Assert.Single(Reopened().LoadRunsWithoutASubmission());

        store.RunEnded(run.Id, costSpent: 148_233, Spent, toolCalls: 3, entriesLost: 0, Noon.AddMinutes(4));

        // And gone from that reading once it has ended, so the next start-up does not go looking for an
        // agent that is finished. The ending and the figures are one statement: a stop between them
        // would leave a run read as in progress beside the figures it ended on.
        Assert.Empty(Reopened().LoadRunsWithoutASubmission());
    }

    [Fact]
    public void RunWithoutASubmission_IsInNoListOfSubmissions()
    {
        var store = Reopened();

        store.Add(ASubmission("Ada Lovelace wrote the first program.", Noon));
        store.AddRun(AQuestionsRun());

        // A question is not a submission, and its run hangs off none: `Load` answers with the
        // submissions and what they were given, and a question's run is in neither (research.md R-12).
        var read = Assert.Single(Reopened().Load());

        Assert.Null(read.Run);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-006")]
    public void OlderFile_IsRebuilt_WhenThe002QueueWroteIt()
    {
        var submissionId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        // `submission_id` NOT NULL and none of the figures: the schema `002-ingest-queue` left
        // (git show 1c5f1cd:src/Grimoire.Runs/Adapters/SqliteSubmissionStore.cs).
        WriteAFile(submissionId, runId, "The 002 queue wrote this.", columnsAdded: []);

        var store = Reopened();
        var read = Assert.Single(store.Load());

        Assert.Equal(submissionId, read.Id);
        Assert.Equal("The 002 queue wrote this.", read.Text);
        Assert.Equal(Noon, read.SubmittedAt);
        Assert.Equal(SubmissionState.Done, read.State);
        Assert.Equal(runId, read.Run!.Id);
        Assert.Equal(submissionId, read.Run.QueuedId);
        Assert.Equal(ToolGrant.ForIngest, read.Run.GrantedTools);
        Assert.Equal(new AgentProcessIdentity(4242, Noon), read.Run.AgentProcess);

        // The figures it never had come back as the zero an older run is worth, as before the rebuild
        // (DEC-031).
        Assert.Equal(0, read.Run.CostSpent);
        Assert.Equal(new ModelTokens(0, 0, 0, 0), read.Run.Tokens);

        AssertRebuiltToTakeAQuestionsRun(store);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    [Trait("req", "RUNS-006")]
    public void OlderFile_IsRebuilt_WhenThe003RecordLeftIt()
    {
        var submissionId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        // The schema `003-live-run-record` left on main: 002's table, `submission_id` still NOT NULL,
        // and the eight columns 003 added with `ALTER TABLE` — in the order it added them, which is the
        // order they sit in on disk.
        WriteAFile(submissionId, runId, "The 003 record wrote this.", columnsAdded: Columns003Added);

        var store = Reopened();
        var read = Assert.Single(store.Load());

        Assert.Equal(submissionId, read.Id);
        Assert.Equal("The 003 record wrote this.", read.Text);
        Assert.Equal(runId, read.Run!.Id);

        // **Every figure survived the rebuild.** A rebuild that recreated the table from the base
        // `CREATE` would carry the seven columns 002 had and lose these eight to their defaults —
        // which is why the new table is read off the old one, column for column (DEC-031 as amended).
        Assert.Equal(PinnedModel, read.Run.Model);
        Assert.Equal(148_233, read.Run.CostSpent);
        Assert.Equal(Spent, read.Run.Tokens);
        Assert.Equal(17, read.Run.ToolCalls);
        Assert.Equal(2, read.Run.EntriesLost);

        AssertRebuiltToTakeAQuestionsRun(store);
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public void CopyBeforeTheRebuild_HoldsTheFileAsItWas()
    {
        WriteAFile(Guid.NewGuid(), Guid.NewGuid(), "The 003 record wrote this.", columnsAdded: Columns003Added);
        AddASubmissionByHand("And this one never ran.");

        _ = Reopened();

        // The copy is the file **before** the rebuild: both submissions, the run, and `submission_id`
        // still declared NOT NULL — what the owner goes back to if the rebuilt file is not what they
        // had (DEC-031 as amended).
        using var copy = OpenTheCopy();

        Assert.Equal(2L, Count(copy, "SELECT COUNT(*) FROM submissions"));
        Assert.Equal(1L, Count(copy, "SELECT COUNT(*) FROM runs"));
        Assert.Equal(1L, Count(copy, "SELECT \"notnull\" FROM pragma_table_info('runs') WHERE name = 'submission_id'"));
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public void FailedRebuild_NamesTheCopy()
    {
        WriteAFile(Guid.NewGuid(), Guid.NewGuid(), "The 002 queue wrote this.", columnsAdded: []);

        // The rebuild creates `runs_new`; one already there makes its transaction fail.
        LeaveARunsNewTableBehind();

        var refused = Assert.Throws<InvalidOperationException>(Reopened);

        // The error names the copy, and the copy is there and holds what the file held.
        Assert.Contains(CopyPath, refused.Message, StringComparison.Ordinal);

        using var copy = OpenTheCopy();

        Assert.Equal(1L, Count(copy, "SELECT COUNT(*) FROM submissions"));

        // And the file is as it was: the transaction rolled back, and nothing stamped it.
        Assert.Equal(0L, UserVersion());
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public void SecondRebuild_NamesAFreshCopy_AfterTheFirstFailed()
    {
        WriteAFile(Guid.NewGuid(), Guid.NewGuid(), "The 002 queue wrote this.", columnsAdded: []);
        LeaveARunsNewTableBehind();

        Assert.Throws<InvalidOperationException>(Reopened);

        // The file changes before the next attempt, as the owner's file would while they look into it.
        AddASubmissionByHand("Added after the first attempt.");

        var refused = Assert.Throws<InvalidOperationException>(Reopened);
        Assert.Contains(CopyPath, refused.Message, StringComparison.Ordinal);

        // **The copy the error names is the file as it was before this attempt**, not the one before
        // the first: a stale copy named by an error would mislead (DEC-031, clarified after closing 004).
        using var copy = OpenTheCopy();

        Assert.Equal(2L, Count(copy, "SELECT COUNT(*) FROM submissions"));
    }

    [Fact]
    [Trait("req", "RUNS-004")]
    public void StampedFile_IsNotInspected_WhereAColumnIsMissing()
    {
        // A file this Grimoire wrote, stamped `user_version` 1, from which a column has since gone.
        _ = Reopened();

        using (var connection = OpenTheFile())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "ALTER TABLE runs DROP COLUMN entries_lost";
            command.ExecuteNonQuery();
        }

        // Opening it does not repair it: once stamped, the store reads the number and nothing else
        // (DEC-031, clarified after closing 004).
        var store = Reopened();

        using (var connection = OpenTheFile())
        {
            Assert.Equal(0L, Count(connection, "SELECT COUNT(*) FROM pragma_table_info('runs') WHERE name = 'entries_lost'"));
        }

        // It is an error, and the error says which column the file lacks.
        var failed = Assert.Throws<SqliteException>(() => store.Load());

        Assert.Contains("no such column", failed.Message, StringComparison.Ordinal);
        Assert.Contains("entries_lost", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What both fixtures must show once reopened: a question's run is accepted and read back, the
    /// file reads schema 1, and the copy taken before the rebuild lies beside it (DEC-031 as amended).
    /// </summary>
    private void AssertRebuiltToTakeAQuestionsRun(SqliteSubmissionStore store)
    {
        var question = AQuestionsRun();
        store.AddRun(question);

        Assert.Equal(question.Id, Assert.Single(Reopened().LoadRunsWithoutASubmission()).Id);
        Assert.Equal(1L, UserVersion());
        Assert.True(File.Exists(Path.Combine(directory, "submissions.db.before-rebuild")));
    }

    /// <summary>The columns <c>003-live-run-record</c> added to <c>runs</c>, in the order it added them.</summary>
    private static readonly string[] Columns003Added =
    [
        "model TEXT NOT NULL DEFAULT ''",
        "cost_spent INTEGER NOT NULL DEFAULT 0",
        "input_tokens INTEGER NOT NULL DEFAULT 0",
        "output_tokens INTEGER NOT NULL DEFAULT 0",
        "cache_read_tokens INTEGER NOT NULL DEFAULT 0",
        "cache_write_tokens INTEGER NOT NULL DEFAULT 0",
        "tool_calls INTEGER NOT NULL DEFAULT 0",
        "entries_lost INTEGER NOT NULL DEFAULT 0",
    ];

    private string CopyPath => Path.Combine(directory, "submissions.db.before-rebuild");

    private SqliteConnection OpenTheCopy()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = CopyPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());

        connection.Open();
        return connection;
    }

    private static long Count(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private void AddASubmissionByHand(string text)
    {
        using var connection = OpenTheFile();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO submissions VALUES ($id, $text, $at, 'submitted', NULL, NULL)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$at", Noon.ToString("O"));
        command.ExecuteNonQuery();
    }

    private void LeaveARunsNewTableBehind()
    {
        using var connection = OpenTheFile();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE runs_new (id TEXT)";
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenTheFile()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "submissions.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());

        connection.Open();
        return connection;
    }

    private long UserVersion()
    {
        using var connection = OpenTheFile();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// A file an older Grimoire wrote, written by hand with no help from the adapter under test — a
    /// fixture the adapter built would not be an older file at all. 002's two tables as it created
    /// them, then whatever later columns that Grimoire added, and one submission with its run; where
    /// the figures' columns exist they hold figures that are not zero.
    /// </summary>
    private void WriteAFile(Guid submissionId, Guid runId, string text, string[] columnsAdded)
    {
        using var connection = OpenTheFile();
        using var command = connection.CreateCommand();

        var added = string.Concat(columnsAdded.Select(c => $"ALTER TABLE runs ADD COLUMN {c};\n"));
        var figures = columnsAdded.Length == 0
            ? string.Empty
            : $"""
                UPDATE runs SET model = '{PinnedModel}', cost_spent = 148233,
                    input_tokens = {Spent.InputTokens}, output_tokens = {Spent.OutputTokens},
                    cache_read_tokens = {Spent.CacheReadInputTokens},
                    cache_write_tokens = {Spent.CacheCreationInputTokens},
                    tool_calls = 17, entries_lost = 2;
                """;

        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS submissions (
                id              TEXT PRIMARY KEY,
                text            TEXT NOT NULL,
                submitted_at    TEXT NOT NULL,
                state           TEXT NOT NULL,
                run_id          TEXT NULL,
                acknowledged_at TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS runs (
                id                       TEXT PRIMARY KEY,
                submission_id            TEXT NOT NULL,
                started_at               TEXT NOT NULL,
                granted_tools            TEXT NOT NULL,
                grant_recorded_at        TEXT NOT NULL,
                agent_process_id         INTEGER NULL,
                agent_process_started_at TEXT NULL
            );

            {added}
            INSERT INTO submissions VALUES
                ('{submissionId}', '{text}', '{Noon:O}', 'done', '{runId}', NULL);

            INSERT INTO runs (id, submission_id, started_at, granted_tools, grant_recorded_at,
                              agent_process_id, agent_process_started_at)
            VALUES ('{runId}', '{submissionId}', '{Noon:O}',
                    '{string.Join('\n', ToolGrant.ForIngest)}', '{Noon:O}', 4242, '{Noon:O}');

            {figures}
            """;

        command.ExecuteNonQuery();
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

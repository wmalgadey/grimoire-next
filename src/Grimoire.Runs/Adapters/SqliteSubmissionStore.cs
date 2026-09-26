using System.Globalization;
using Grimoire.Agent;
using Microsoft.Data.Sqlite;

namespace Grimoire.Runs.Adapters;

/// <summary>
/// The submissions on disk, in one SQLite file inside a directory Grimoire owns.
/// </summary>
/// <remarks>
/// <para>
/// The only file in the repository that names SQLite (Constitution V.2). Raw SQL over two tables,
/// which is why there is no ORM.
/// </para>
/// <para>
/// <b>The run table gains columns, without a migration framework.</b> DEC-023 turned a migration
/// <em>mechanism</em> down as having no consumer, and that still holds — but a consumer for bringing
/// an existing file up to date exists now: the owner's own <c>submissions.db</c>, holding the ingests
/// they have already made. The alternative is asking them to delete it, which throws their list away
/// to save eight lines. So: <c>CREATE TABLE IF NOT EXISTS</c> as before, then
/// <c>PRAGMA table_info(runs)</c> and one <c>ALTER TABLE runs ADD COLUMN</c> for each column that is
/// not there. No version table and no ordered scripts. That a committed <c>ALTER TABLE</c> survives is
/// SQLite's decision and is not tested; that an older file comes back with its submissions intact and
/// its figures at zero is ours, and the Contract suite proves it (research.md R-07).
/// </para>
/// <para>
/// Every statement here commits before the call returns, which is SQLite's own default and the
/// whole reason it was chosen: RUNS-004 covers a stop that gives Grimoire no chance to act, so
/// nothing may be waiting to be written. That a committed transaction survives the process dying
/// is SQLite's decision and is not tested here — we test the decisions we made (III.8,
/// research.md R-02).
/// </para>
/// <para>
/// Times are ISO 8601 UTC text, the same way the wiki's <c>generated.at</c> and the browser's list
/// already show them. Nothing in Grimoire needs an offset applied before two times can be compared.
/// </para>
/// </remarks>
public sealed class SqliteSubmissionStore : ISubmissionStore
{
    /// <summary>
    /// The tool names of a grant, kept as one field. They are read back as a list and never
    /// searched or joined on, so a table of their own would be shape with no consumer (II.1).
    /// </summary>
    private const char ToolSeparator = '\n';

    private readonly string connectionString;

    /// <summary>
    /// Creates the file and its two tables where they are not there yet. The directory is
    /// Grimoire's own and never the wiki: the queue writes nothing into the wiki, and Grimoire's
    /// bookkeeping inside the user's repository would turn up in their version history
    /// (contracts/submission-store.md).
    /// </summary>
    public SqliteSubmissionStore(string directory)
    {
        Directory.CreateDirectory(directory);

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "submissions.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        Execute("""
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
            """);

        BringTheRunTableUpToDate();
    }

    /// <summary>
    /// The columns this Grimoire needs on <c>runs</c>, added where an older file does not have them
    /// (research.md R-07).
    /// </summary>
    /// <remarks>
    /// The default is what an older run's figures are worth: it ran before Grimoire counted, so nothing
    /// is known about what it spent, and zero is the only honest answer a column can give. The model is
    /// the one thing that cannot be defaulted honestly, so it is left empty rather than guessed at —
    /// the current <c>--model</c> would claim an older run had used a model it may never have seen.
    /// </remarks>
    private void BringTheRunTableUpToDate()
    {
        var wanted = new (string Column, string Definition)[]
        {
            ("model", "TEXT NOT NULL DEFAULT ''"),
            ("tokens_used", "INTEGER NOT NULL DEFAULT 0"),
            ("tool_calls", "INTEGER NOT NULL DEFAULT 0"),
            ("entries_lost", "INTEGER NOT NULL DEFAULT 0"),
        };

        var present = ColumnsOfTheRunTable();

        foreach (var (column, definition) in wanted.Where(c => !present.Contains(c.Column)))
        {
            // Not parameterised, and it cannot be: a column name is not a value, and SQLite takes no
            // parameter in that position. Every name here is a literal in this file, so nothing the
            // user or the agent ever touches reaches it.
            Execute($"ALTER TABLE runs ADD COLUMN {column} {definition}");
        }
    }

    private HashSet<string> ColumnsOfTheRunTable()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT name FROM pragma_table_info('runs')";

        using var rows = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);

        while (rows.Read())
        {
            columns.Add(rows.GetString(0));
        }

        return columns;
    }

    public IReadOnlyList<StoredSubmission> Load()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        // In the order they were accepted — the queue's order (RUNS-002). `rowid` is that order
        // and `submitted_at` only usually is: the clock those stamps come from is not monotonic,
        // and a correction between two submissions would hand them back the wrong way round, so
        // the queue this Grimoire rebuilds would not be the queue the last one had. Nothing is
        // ever deleted here, so rowids only ever climb. The left join is what makes a submission
        // that never ran and one that did come back through the same read.
        command.CommandText = """
            SELECT s.id, s.text, s.submitted_at, s.state, s.acknowledged_at,
                   r.id, r.submission_id, r.started_at, r.granted_tools, r.grant_recorded_at,
                   r.agent_process_id, r.agent_process_started_at,
                   r.model, r.tokens_used, r.tool_calls, r.entries_lost
            FROM submissions s
            LEFT JOIN runs r ON r.id = s.run_id
            ORDER BY s.rowid
            """;

        using var rows = command.ExecuteReader();
        var held = new List<StoredSubmission>();

        while (rows.Read())
        {
            held.Add(new StoredSubmission(
                Guid.Parse(rows.GetString(0)),
                rows.GetString(1),
                Moment(rows.GetString(2)),
                StateOf(rows.GetString(3)),
                rows.IsDBNull(5) ? null : RunIn(rows),
                rows.IsDBNull(4) ? null : Moment(rows.GetString(4))));
        }

        return held;
    }

    private static StoredRun RunIn(SqliteDataReader rows) => new(
        Guid.Parse(rows.GetString(5)),
        Guid.Parse(rows.GetString(6)),
        Moment(rows.GetString(7)),
        rows.GetString(8).Split(ToolSeparator, StringSplitOptions.RemoveEmptyEntries),
        Moment(rows.GetString(9)),
        rows.GetString(12),

        // Both halves or neither: an identifier without the moment its process started is not an
        // identity, and acting on one would be acting on a number (research.md R-11).
        rows.IsDBNull(10) || rows.IsDBNull(11)
            ? null
            : new AgentProcessIdentity(rows.GetInt32(10), Moment(rows.GetString(11))),
        rows.GetInt64(13),
        rows.GetInt32(14),
        rows.GetInt32(15));

    public void Add(StoredSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        Execute(
            """
            INSERT INTO submissions (id, text, submitted_at, state, run_id, acknowledged_at)
            VALUES ($id, $text, $submitted_at, $state, NULL, NULL)
            """,
            ("$id", submission.Id.ToString()),
            ("$text", submission.Text),
            ("$submitted_at", Text(submission.SubmittedAt)),
            ("$state", WireNameOf(submission.State)));
    }

    /// <summary>
    /// The run's own record and the identifier onto its submission, in one transaction: a store
    /// holding a submission that names a run it does not have would be a state nothing could read.
    /// </summary>
    public void AssignRun(Guid submissionId, StoredRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        Execute(
            """
            INSERT INTO runs (id, submission_id, started_at, granted_tools, grant_recorded_at,
                              agent_process_id, agent_process_started_at,
                              model, tokens_used, tool_calls, entries_lost)
            VALUES ($run, $submission, $started_at, $tools, $recorded_at, NULL, NULL,
                    $model, $tokens, $calls, $lost);

            UPDATE submissions SET run_id = $run WHERE id = $submission;
            """,
            ("$run", run.Id.ToString()),
            ("$submission", submissionId.ToString()),
            ("$started_at", Text(run.StartedAt)),
            ("$tools", string.Join(ToolSeparator, run.GrantedTools)),
            ("$recorded_at", Text(run.GrantRecordedAt)),
            ("$model", run.Model),
            ("$tokens", run.TokensUsed),
            ("$calls", run.ToolCalls),
            ("$lost", run.EntriesLost));
    }

    public void RecordAgentProcess(Guid runId, AgentProcessIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Execute(
            """
            UPDATE runs SET agent_process_id = $process, agent_process_started_at = $started_at
            WHERE id = $run
            """,
            ("$process", identity.ProcessId),
            ("$started_at", Text(identity.StartedAt)),
            ("$run", runId.ToString()));
    }

    /// <summary>
    /// All three figures in one statement, because one event writes them and one row reads them
    /// (RUNS-010, RUNS-007).
    /// </summary>
    public void RecordFigures(Guid runId, long tokensUsed, int toolCalls, int entriesLost) =>
        Execute(
            """
            UPDATE runs SET tokens_used = $tokens, tool_calls = $calls, entries_lost = $lost
            WHERE id = $run
            """,
            ("$tokens", tokensUsed),
            ("$calls", toolCalls),
            ("$lost", entriesLost),
            ("$run", runId.ToString()));

    public void SetState(Guid submissionId, SubmissionState state) =>
        Execute(
            "UPDATE submissions SET state = $state WHERE id = $id",
            ("$state", WireNameOf(state)),
            ("$id", submissionId.ToString()));

    public void Acknowledge(Guid submissionId, DateTimeOffset at) =>
        Execute(
            "UPDATE submissions SET acknowledged_at = $at WHERE id = $id",
            ("$at", Text(at)),
            ("$id", submissionId.ToString()));

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// One statement or several, as one transaction, committed before this returns. There is no
    /// flush and no write at close: nothing may be waiting to be written when a kill lands.
    /// </summary>
    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>The wire names the browser already uses, so the file reads as the page does.</summary>
    private static string WireNameOf(SubmissionState state) => state switch
    {
        SubmissionState.Submitted => "submitted",
        SubmissionState.Running => "running",
        SubmissionState.Done => "done",
        SubmissionState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "not one of the four states"),
    };

    private static SubmissionState StateOf(string wire) => wire switch
    {
        "submitted" => SubmissionState.Submitted,
        "running" => SubmissionState.Running,
        "done" => SubmissionState.Done,
        "failed" => SubmissionState.Failed,

        // A file this Grimoire cannot read is not a file to guess at: every submission the user
        // made is in it, and reading one of them wrongly is worse than refusing to start.
        _ => throw new InvalidOperationException($"the store holds a state Grimoire does not know: {wire}"),
    };

    private static string Text(DateTimeOffset moment) =>
        moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Moment(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

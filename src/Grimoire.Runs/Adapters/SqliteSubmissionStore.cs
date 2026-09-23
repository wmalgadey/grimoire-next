using System.Globalization;
using Grimoire.Agent;
using Microsoft.Data.Sqlite;

namespace Grimoire.Runs.Adapters;

/// <summary>
/// The submissions on disk, in one SQLite file inside a directory Grimoire owns.
/// </summary>
/// <remarks>
/// <para>
/// The only file in the repository that names SQLite (Constitution V.2). Raw SQL over two tables
/// that do not change shape in this feature, which is why there is no ORM and no migration
/// mechanism — one would be a mechanism with no consumer (II.1, research.md R-01).
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
                   r.agent_process_id, r.agent_process_started_at
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

        // Both halves or neither: an identifier without the moment its process started is not an
        // identity, and acting on one would be acting on a number (research.md R-11).
        rows.IsDBNull(10) || rows.IsDBNull(11)
            ? null
            : new AgentProcessIdentity(rows.GetInt32(10), Moment(rows.GetString(11))));

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
                              agent_process_id, agent_process_started_at)
            VALUES ($run, $submission, $started_at, $tools, $recorded_at, NULL, NULL);

            UPDATE submissions SET run_id = $run WHERE id = $submission;
            """,
            ("$run", run.Id.ToString()),
            ("$submission", submissionId.ToString()),
            ("$started_at", Text(run.StartedAt)),
            ("$tools", string.Join(ToolSeparator, run.GrantedTools)),
            ("$recorded_at", Text(run.GrantRecordedAt)));
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

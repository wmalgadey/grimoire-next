using System.Globalization;
using Grimoire.Ingest;
using Grimoire.Wiki;
using Microsoft.Data.Sqlite;

namespace Grimoire.Tasks.Adapters;

/// <summary>A page of the task list plus the cursor that continues it.</summary>
/// <param name="Tasks">Newest first (FR-030).</param>
/// <param name="NextCursor">Opaque; <c>null</c> when this page is the last.</param>
public sealed record TaskPage(IReadOnlyList<Task> Tasks, string? NextCursor);

/// <summary>
/// The operational store (ADR-0006). This file is the only place in the repository that
/// references <c>Microsoft.Data.Sqlite</c> — adapter confinement asserted by
/// <c>tests/architecture/AdapterConfinementTests.cs</c> (constitution V.3).
/// </summary>
public sealed class SqliteStore : IDisposable
{
    private readonly SqliteConnection _connection;

    // One connection, many callers: the request threads reading the task list and task view,
    // and the dispatcher's thread appending tool calls and ending the run. A SqliteConnection
    // is not safe to share without serialising them, and disposing it under a transaction
    // another thread is committing throws from inside the driver. Every public member holds
    // this gate for the whole of its work, Dispose included, so a call is either complete or
    // refused — never torn apart by a host shutdown.
    private readonly Lock _gate = new();

    /// <param name="databasePath">
    /// Path to the SQLite file, from <c>GRIMOIRE_STATE_DB</c> — a volume on local block storage,
    /// never a network filesystem (contracts/deployment.md).
    /// </param>
    public SqliteStore(string databasePath)
    {
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
        }.ToString());
        _connection.Open();
    }

    /// <summary>Creates the six tables if they are not there. Idempotent.</summary>
    public void EnsureSchema()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SqliteSchema.Ddl;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Records an accepted submission as exactly one task with its source (FR-002, FR-004).</summary>
    public void AddTask(Task task)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            Execute(transaction,
                """
                INSERT INTO task (id, state, submitted_at, started_at, ended_at, failure_reason)
                VALUES ($id, $state, $submittedAt, $startedAt, $endedAt, $failureReason);
                """,
                ("$id", task.Id),
                ("$state", StateToDb(task.State)),
                ("$submittedAt", Time(task.SubmittedAt)),
                ("$startedAt", Time(task.StartedAt)),
                ("$endedAt", Time(task.EndedAt)),
                ("$failureReason", task.FailureReason));

            Execute(transaction,
                """
                INSERT INTO source (task_id, kind, submitted_value, retrieved_text, retrieved_at, byte_length)
                VALUES ($taskId, $kind, $submittedValue, $retrievedText, $retrievedAt, $byteLength);
                """,
                ("$taskId", task.Id),
                ("$kind", task.Source.Kind is SourceKind.Text ? "text" : "url"),
                ("$submittedValue", task.Source.SubmittedValue),
                ("$retrievedText", task.Source.RetrievedText),
                ("$retrievedAt", Time(task.Source.RetrievedAt)),
                ("$byteLength", task.Source.ByteLength));

            transaction.Commit();
        }
    }

    /// <summary>
    /// Attaches the text retrieved for a URL source before the run is dispatched (FR-003).
    /// </summary>
    public void AttachRetrievedText(string taskId, string retrievedText, DateTimeOffset retrievedAt, int byteLength)
    {
        lock (_gate)
        {
            Execute(null,
                """
                UPDATE source
                   SET retrieved_text = $text, retrieved_at = $at, byte_length = $byteLength
                 WHERE task_id = $taskId;
                """,
                ("$text", retrievedText),
                ("$at", Time(retrievedAt)),
                ("$byteLength", byteLength),
                ("$taskId", taskId));
        }
    }

    /// <summary>
    /// Opens the task's one and only run and moves it to <see cref="TaskState.Running"/>.
    /// A second call for the same task is rejected by the UNIQUE constraint on
    /// <c>agent_run.task_id</c>, not by code (FR-005).
    /// </summary>
    public void StartRun(string taskId, AgentRun run, DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            var runId = ExecuteScalar(transaction,
                """
                INSERT INTO agent_run (task_id, instruction_path, instruction_sha256, instruction_bytes, started_at)
                VALUES ($taskId, $path, $sha, $bytes, $startedAt)
                RETURNING id;
                """,
                ("$taskId", taskId),
                ("$path", run.InstructionVersion.Path),
                ("$sha", run.InstructionVersion.Sha256),
                ("$bytes", run.InstructionVersion.ByteLength),
                ("$startedAt", Time(startedAt)));

            Execute(transaction,
                """
                INSERT INTO tool_grant (run_id, tools, recorded_at)
                VALUES ($runId, $tools, $recordedAt);
                """,
                ("$runId", runId),
                ("$tools", string.Join('\n', run.ToolGrant.Tools)),
                ("$recordedAt", Time(run.ToolGrant.RecordedAt)));

            Execute(transaction,
                """
                UPDATE task SET state = 'running', started_at = $startedAt WHERE id = $taskId;
                """,
                ("$startedAt", Time(startedAt)),
                ("$taskId", taskId));

            transaction.Commit();
        }
    }

    /// <summary>
    /// Appends one tool call to the run's record, refusals included (FR-011, FR-021). The record
    /// is only ever inserted into: it is append-only during the run and frozen after (FR-023).
    /// </summary>
    public void AppendToolCall(string taskId, ToolCall call)
    {
        lock (_gate)
        {
            Execute(null,
                """
                INSERT INTO tool_call (run_id, seq, tool, target, outcome, detail, at)
                VALUES ((SELECT id FROM agent_run WHERE task_id = $taskId), $seq, $tool, $target, $outcome, $detail, $at);
                """,
                ("$taskId", taskId),
                ("$seq", call.Seq),
                ("$tool", call.Tool),
                ("$target", call.Target),
                ("$outcome", OutcomeToDb(call.Outcome)),
                ("$detail", call.Detail),
                ("$at", Time(call.At)));
        }
    }

    /// <summary>
    /// Closes the run and the task together. A failed run carries a reason and no commit
    /// (FR-017, FR-018); a completed run carries at most one commit (FR-015, FR-016).
    /// </summary>
    public void EndRun(
        string taskId,
        RunOutcomeKind outcome,
        string? failureReason,
        WikiCommit? commit,
        int? durationMs,
        DateTimeOffset endedAt)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            Execute(transaction,
                """
                UPDATE agent_run
                   SET outcome = $outcome,
                       failure_reason = $failureReason,
                       commit_sha = $sha,
                       commit_parent_sha = $parentSha,
                       commit_message = $message,
                       commit_committed_at = $committedAt,
                       duration_ms = $durationMs
                 WHERE task_id = $taskId;
                """,
                ("$outcome", outcome is RunOutcomeKind.Completed ? "completed" : "failed"),
                ("$failureReason", failureReason),
                ("$sha", commit?.Sha),
                ("$parentSha", commit?.ParentSha),
                ("$message", commit?.Message),
                ("$committedAt", Time(commit?.CommittedAt)),
                ("$durationMs", durationMs),
                ("$taskId", taskId));

            Execute(transaction,
                """
                UPDATE task SET state = $state, ended_at = $endedAt, failure_reason = $failureReason
                 WHERE id = $taskId;
                """,
                ("$state", outcome is RunOutcomeKind.Completed ? "completed" : "failed"),
                ("$endedAt", Time(endedAt)),
                ("$failureReason", failureReason),
                ("$taskId", taskId));

            transaction.Commit();
        }
    }

    /// <summary>
    /// Fails a task before any run was dispatched — URL retrieval refused or failed (FR-003) —
    /// or a task startup recovery found stranded in <see cref="TaskState.Running"/> (FR-028).
    /// </summary>
    public void FailTask(string taskId, string failureReason, DateTimeOffset endedAt)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            Execute(transaction,
                """
                UPDATE agent_run
                   SET outcome = 'failed', failure_reason = $failureReason
                 WHERE task_id = $taskId AND outcome IS NULL;
                """,
                ("$failureReason", failureReason),
                ("$taskId", taskId));

            Execute(transaction,
                """
                UPDATE task SET state = 'failed', ended_at = $endedAt, failure_reason = $failureReason
                 WHERE id = $taskId;
                """,
                ("$endedAt", Time(endedAt)),
                ("$failureReason", failureReason),
                ("$taskId", taskId));

            transaction.Commit();
        }
    }

    /// <summary>
    /// Writes the revert record and sets the state to <see cref="TaskState.Reverted"/>. Once a run
    /// has ended these are the only permitted writes to the task (FR-023).
    /// </summary>
    public void RecordRevert(string taskId, RevertRecord revert)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            Execute(transaction,
                """
                INSERT INTO revert_record (task_id, revert_commit_sha, reverted_at)
                VALUES ($taskId, $sha, $at);
                """,
                ("$taskId", taskId),
                ("$sha", revert.RevertCommitSha),
                ("$at", Time(revert.RevertedAt)));

            Execute(transaction,
                "UPDATE task SET state = 'reverted' WHERE id = $taskId;",
                ("$taskId", taskId));

            transaction.Commit();
        }
    }

    /// <summary>Reads one task whole, in whatever state it is in (FR-020).</summary>
    public Task? GetTask(string taskId)
    {
        lock (_gate)
        {
            var tasks = ReadTasks("WHERE t.id = $taskId", [("$taskId", taskId)]);
            return tasks.Count is 0 ? null : tasks[0];
        }
    }

    /// <summary>
    /// The task list: every retained task, newest first, paged by an opaque cursor, so closing
    /// the browser loses access to no task (FR-030, SC-006).
    /// </summary>
    public TaskPage ListTasks(int limit, string? cursor)
    {
        lock (_gate)
        {
            var parameters = new List<(string, object?)>();
            var where = string.Empty;

            if (cursor is not null)
            {
                var (submittedAt, id) = DecodeCursor(cursor);
                where = "WHERE (t.submitted_at, t.id) < ($cursorAt, $cursorId)";
                parameters.Add(("$cursorAt", submittedAt));
                parameters.Add(("$cursorId", id));
            }

            // One extra row tells us whether there is a next page without a second query.
            var rows = ReadTasks(where, parameters, limit + 1);
            var page = rows.Count > limit ? rows.Take(limit).ToList() : rows;
            var next = rows.Count > limit ? EncodeCursor(page[^1]) : null;
            return new TaskPage(page, next);
        }
    }

    /// <summary>
    /// Tasks in one state, oldest first — the queue in <c>submittedAt</c> order (FR-019) and the
    /// tasks startup recovery must fail (FR-028).
    /// </summary>
    public IReadOnlyList<Task> ListTasksInState(TaskState state)
    {
        lock (_gate)
        {
            return ReadTasks("WHERE t.state = $state", [("$state", StateToDb(state))], ascending: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    private IReadOnlyList<Task> ReadTasks(
        string where,
        IReadOnlyList<(string Name, object? Value)> parameters,
        int? limit = null,
        bool ascending = false)
    {
        var order = ascending ? "ASC" : "DESC";
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT t.id, t.state, t.submitted_at, t.started_at, t.ended_at, t.failure_reason,
                   s.kind, s.submitted_value, s.retrieved_text, s.retrieved_at, s.byte_length,
                   r.id, r.instruction_path, r.instruction_sha256, r.instruction_bytes,
                   r.outcome, r.failure_reason, r.commit_sha, r.commit_parent_sha,
                   r.commit_message, r.commit_committed_at, r.duration_ms,
                   g.tools, g.recorded_at,
                   v.revert_commit_sha, v.reverted_at
              FROM task t
              JOIN source s ON s.task_id = t.id
              LEFT JOIN agent_run r ON r.task_id = t.id
              LEFT JOIN tool_grant g ON g.run_id = r.id
              LEFT JOIN revert_record v ON v.task_id = t.id
            {where}
             ORDER BY t.submitted_at {order}, t.id {order}
            {(limit is null ? string.Empty : "LIMIT $limit")};
            """;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        if (limit is not null)
        {
            command.Parameters.AddWithValue("$limit", limit.Value);
        }

        var tasks = new List<Task>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader, out _));
            }
        }

        // Tool calls are read per run so the ordered record comes back ordered, and the count
        // the run limit is measured against is that record's length rather than a second number
        // that could drift from it (FR-021, FR-009).
        var withCalls = new List<Task>(tasks.Count);
        foreach (var task in tasks)
        {
            if (task.Run is null)
            {
                withCalls.Add(task);
                continue;
            }

            var calls = ReadToolCalls(task.Id);
            withCalls.Add(task with { Run = task.Run with { ToolCalls = calls, ToolCallCount = calls.Count } });
        }

        return withCalls;
    }

    private Task ReadTask(SqliteDataReader reader, out long? runId)
    {
        var source = new Source(
            reader.GetString(6) is "text" ? SourceKind.Text : SourceKind.Url,
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : ParseTime(reader.GetString(9)),
            reader.GetInt32(10));

        AgentRun? run = null;
        runId = null;
        if (!reader.IsDBNull(11))
        {
            runId = reader.GetInt64(11);
            var commit = reader.IsDBNull(17)
                ? null
                : new WikiCommit(
                    reader.GetString(17),
                    reader.GetString(18),
                    reader.GetString(19),
                    ParseTime(reader.GetString(20)));

            run = new AgentRun(
                new InstructionVersion(reader.GetString(12), reader.GetString(13), reader.GetInt32(14)),
                new ToolGrant(
                    reader.IsDBNull(22) ? [] : reader.GetString(22).Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    reader.IsDBNull(23) ? default : ParseTime(reader.GetString(23))),
                [],
                reader.IsDBNull(15) ? null : reader.GetString(15) is "completed" ? RunOutcomeKind.Completed : RunOutcomeKind.Failed,
                reader.IsDBNull(16) ? null : reader.GetString(16),
                commit,
                0,
                reader.IsDBNull(21) ? null : reader.GetInt32(21));
        }

        return new Task(
            reader.GetString(0),
            StateFromDb(reader.GetString(1)),
            ParseTime(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseTime(reader.GetString(3)),
            reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
            source,
            run,
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(24) ? null : new RevertRecord(reader.GetString(24), ParseTime(reader.GetString(25))));
    }

    private List<ToolCall> ReadToolCalls(string taskId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT c.seq, c.tool, c.target, c.outcome, c.detail, c.at
              FROM tool_call c
              JOIN agent_run r ON r.id = c.run_id
             WHERE r.task_id = $taskId
             ORDER BY c.seq ASC;
            """;
        command.Parameters.AddWithValue("$taskId", taskId);

        var calls = new List<ToolCall>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            calls.Add(new ToolCall(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                OutcomeFromDb(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ParseTime(reader.GetString(5))));
        }

        return calls;
    }

    private void Execute(SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private long ExecuteScalar(SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (long)command.ExecuteScalar()!;
    }

    // Timestamps are stored as ISO-8601 UTC so lexical order is chronological order — which is
    // what makes the newest-first cursor a plain tuple comparison.
    private static string? Time(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.ParseExact(value, "yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static string EncodeCursor(Task task) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Time(task.SubmittedAt)}|{task.Id}"));

    private static (string SubmittedAt, string Id) DecodeCursor(string cursor)
    {
        string decoded;
        try
        {
            decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            throw new ArgumentException("The cursor is not a cursor this store issued.", nameof(cursor));
        }

        var separator = decoded.LastIndexOf('|');
        if (separator < 0)
        {
            throw new ArgumentException("The cursor is not a cursor this store issued.", nameof(cursor));
        }

        return (decoded[..separator], decoded[(separator + 1)..]);
    }

    private static string StateToDb(TaskState state) => state switch
    {
        TaskState.Queued => "queued",
        TaskState.Running => "running",
        TaskState.Completed => "completed",
        TaskState.Failed => "failed",
        TaskState.Reverted => "reverted",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static TaskState StateFromDb(string state) => state switch
    {
        "queued" => TaskState.Queued,
        "running" => TaskState.Running,
        "completed" => TaskState.Completed,
        "failed" => TaskState.Failed,
        "reverted" => TaskState.Reverted,
        _ => throw new InvalidOperationException($"Unknown task state in the store: '{state}'."),
    };

    private static string OutcomeToDb(ToolCallOutcome outcome) => outcome switch
    {
        ToolCallOutcome.Ok => "ok",
        ToolCallOutcome.Failed => "failed",
        ToolCallOutcome.Refused => "refused",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static ToolCallOutcome OutcomeFromDb(string outcome) => outcome switch
    {
        "ok" => ToolCallOutcome.Ok,
        "failed" => ToolCallOutcome.Failed,
        "refused" => ToolCallOutcome.Refused,
        _ => throw new InvalidOperationException($"Unknown tool-call outcome in the store: '{outcome}'."),
    };
}

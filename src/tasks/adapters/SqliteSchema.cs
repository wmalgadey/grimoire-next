namespace Grimoire.Tasks.Adapters;

/// <summary>
/// The seven tables the operational store is made of (ADR-0006, data-model "Storage notes").
/// The wiki repository holds content and history; this store holds the task artifact and
/// commit *identities*, never wiki content.
/// </summary>
internal static class SqliteSchema
{
    /// <summary>
    /// Idempotent DDL. Two constraints carry requirements rather than conventions:
    /// <list type="bullet">
    /// <item><c>agent_run.task_id</c> is UNIQUE, so FR-005's "never a second run" is a database
    /// property — a retry after a restart cannot slip through.</item>
    /// <item><c>tool_call</c> is keyed by <c>(run_id, seq)</c> and only ever inserted into, so the
    /// record is append-only and ordered (FR-021, FR-023).</item>
    /// </list>
    /// </summary>
    public const string Ddl = """
        PRAGMA journal_mode = WAL;
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS task (
            id             TEXT PRIMARY KEY,
            state          TEXT NOT NULL,
            submitted_at   TEXT NOT NULL,
            started_at     TEXT NULL,
            ended_at       TEXT NULL,
            failure_reason TEXT NULL,
            CHECK (state IN ('queued', 'running', 'completed', 'failed', 'reverted')),
            CHECK (state <> 'failed' OR failure_reason IS NOT NULL)
        );

        CREATE INDEX IF NOT EXISTS ix_task_submitted_at ON task (submitted_at DESC, id DESC);
        CREATE INDEX IF NOT EXISTS ix_task_state ON task (state);

        CREATE TABLE IF NOT EXISTS source (
            task_id         TEXT PRIMARY KEY REFERENCES task (id) ON DELETE CASCADE,
            kind            TEXT NOT NULL,
            submitted_value TEXT NOT NULL,
            retrieved_text  TEXT NULL,
            retrieved_at    TEXT NULL,
            byte_length     INTEGER NOT NULL,
            CHECK (kind IN ('text', 'url'))
        );

        CREATE TABLE IF NOT EXISTS agent_run (
            id                  INTEGER PRIMARY KEY AUTOINCREMENT,
            task_id             TEXT NOT NULL UNIQUE REFERENCES task (id) ON DELETE CASCADE,
            instruction_path    TEXT NOT NULL,
            instruction_sha256  TEXT NOT NULL,
            instruction_bytes   INTEGER NOT NULL,
            outcome             TEXT NULL,
            failure_reason      TEXT NULL,
            commit_sha          TEXT NULL,
            commit_parent_sha   TEXT NULL,
            commit_message      TEXT NULL,
            commit_committed_at TEXT NULL,
            duration_ms         INTEGER NULL,
            started_at          TEXT NOT NULL,
            CHECK (outcome IS NULL OR outcome IN ('completed', 'failed')),
            CHECK (outcome <> 'failed' OR failure_reason IS NOT NULL),
            CHECK (outcome <> 'failed' OR commit_sha IS NULL)
        );

        CREATE TABLE IF NOT EXISTS tool_grant (
            run_id      INTEGER PRIMARY KEY REFERENCES agent_run (id) ON DELETE CASCADE,
            tools       TEXT NOT NULL,
            recorded_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS tool_call (
            run_id  INTEGER NOT NULL REFERENCES agent_run (id) ON DELETE CASCADE,
            seq     INTEGER NOT NULL,
            tool    TEXT NOT NULL,
            target  TEXT NULL,
            outcome TEXT NOT NULL,
            detail  TEXT NULL,
            at      TEXT NOT NULL,
            PRIMARY KEY (run_id, seq),
            CHECK (seq >= 1),
            CHECK (outcome IN ('ok', 'failed', 'refused'))
        );

        CREATE TABLE IF NOT EXISTS revert_record (
            task_id           TEXT PRIMARY KEY REFERENCES task (id) ON DELETE CASCADE,
            revert_commit_sha TEXT NOT NULL,
            reverted_at       TEXT NOT NULL
        );

        -- A commit on record before the branch moves to it; deleted in the transaction that
        -- records it on its task. A row that survives a restart is a settlement to finish.
        CREATE TABLE IF NOT EXISTS pending_settlement (
            task_id      TEXT PRIMARY KEY REFERENCES task (id) ON DELETE CASCADE,
            kind         TEXT NOT NULL,
            commit_sha   TEXT NOT NULL,
            parent_sha   TEXT NOT NULL,
            message      TEXT NOT NULL,
            committed_at TEXT NOT NULL,
            recorded_at  TEXT NOT NULL,
            CHECK (kind IN ('run-commit', 'revert'))
        );
        """;
}

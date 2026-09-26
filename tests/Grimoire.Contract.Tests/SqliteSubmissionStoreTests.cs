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
[Trait("req", "RUNS-004")]
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

    /// <summary>The model a run is recorded against (DEC-010).</summary>
    private const string PinnedModel = "claude-opus-4-5-20251101";

    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
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
    public void Added_IsReadBack_WithTheTextWholeAndUntidied()
    {
        // Every run receives what the user pasted (INGEST-002), so the file has to keep it that
        // way: the newlines, the quotes and the apostrophes among them.
        const string Pasted = "  # A heading\n\n  \"Quoted\", and O'Brien's — ünïcödé …\n\n";
        Reopened().Add(ASubmission(Pasted, Noon));

        Assert.Equal(Pasted, Assert.Single(Reopened().Load()).Text);
    }

    [Fact]
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
        Assert.Equal(submission.Id, held.SubmissionId);
        Assert.Equal(Noon.AddSeconds(2), held.StartedAt);

        // The grant is recorded for every run (GUARD-003), and once the submission outlives the
        // process the grant has to outlive it too (research.md R-08).
        Assert.Equal(ToolGrant.ForIngest, held.GrantedTools);
        Assert.Equal(Noon.AddSeconds(2), held.GrantRecordedAt);
        Assert.Null(held.AgentProcess);
    }

    [Fact]
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
        store.RecordFigures(run.Id, tokensUsed: 148_233, toolCalls: 9, entriesLost: 2);

        var read = Reopened().Load().Single().Run!;

        Assert.Equal(PinnedModel, read.Model);
        Assert.Equal(148_233, read.TokensUsed);
        Assert.Equal(9, read.ToolCalls);
        Assert.Equal(2, read.EntriesLost);
    }

    [Fact]
    [Trait("req", "RUNS-010")]
    public void Ending_PutsTheTerminalStateAndTheFinalFiguresInTheFileTogether()
    {
        var submission = ASubmission("Ada Lovelace wrote the first program.", Noon);
        var run = ARun(submission.Id, Noon);
        var store = Reopened();

        store.Add(submission);
        store.AssignRun(submission.Id, run);
        store.RecordFigures(run.Id, tokensUsed: 51_094, toolCalls: 4, entriesLost: 0);

        store.Ended(submission.Id, SubmissionState.Failed, run.Id, tokensUsed: 148_233, toolCalls: 9, entriesLost: 2);

        // One change, so a stop can leave the file before it or after it and never between: a
        // submission reading failed beside the figures it had one moment earlier is what RUNS-010
        // forbids, and two writes could not promise otherwise.
        var read = Reopened().Load().Single();

        Assert.Equal(SubmissionState.Failed, read.State);
        Assert.Equal(148_233, read.Run!.TokensUsed);
        Assert.Equal(9, read.Run.ToolCalls);
        Assert.Equal(2, read.Run.EntriesLost);
    }

    [Fact]
    [Trait("req", "RUNS-010")]
    public void OlderFile_ComesBackWithItsSubmissionsIntactAndItsFiguresAtZero()
    {
        var submissionId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        // The four columns this Grimoire wants are not there, because the Grimoire that wrote this file
        // did not have them. The owner's own `submissions.db` is exactly this file, and the alternative
        // to reading it is asking them to delete the list of everything they ever submitted
        // (research.md R-07).
        WriteAFileOfTheOlderSchema(submissionId, runId);

        var read = Assert.Single(Reopened().Load());

        Assert.Equal(submissionId, read.Id);
        Assert.Equal("An older Grimoire wrote this.", read.Text);
        Assert.Equal(SubmissionState.Done, read.State);
        Assert.Equal(runId, read.Run!.Id);
        Assert.Equal(ToolGrant.ForIngest, read.Run.GrantedTools);

        // Nothing is known about what an older run spent, and zero is the only honest answer a column
        // can give. The model is left empty rather than guessed at: the current `--model` would claim
        // the run had used one it may never have seen.
        Assert.Equal(string.Empty, read.Run.Model);
        Assert.Equal(0, read.Run.TokensUsed);
        Assert.Equal(0, read.Run.ToolCalls);
        Assert.Equal(0, read.Run.EntriesLost);
    }

    /// <summary>
    /// A file with the schema <c>002-ingest-queue</c> left, written with no help from the adapter under
    /// test — a fixture the adapter built would not be an older file at all.
    /// </summary>
    private void WriteAFileOfTheOlderSchema(Guid submissionId, Guid runId)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "submissions.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());

        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE submissions (
                id              TEXT PRIMARY KEY,
                text            TEXT NOT NULL,
                submitted_at    TEXT NOT NULL,
                state           TEXT NOT NULL,
                run_id          TEXT NULL,
                acknowledged_at TEXT NULL
            );

            CREATE TABLE runs (
                id                       TEXT PRIMARY KEY,
                submission_id            TEXT NOT NULL,
                started_at               TEXT NOT NULL,
                granted_tools            TEXT NOT NULL,
                grant_recorded_at        TEXT NOT NULL,
                agent_process_id         INTEGER NULL,
                agent_process_started_at TEXT NULL
            );

            INSERT INTO submissions VALUES
                ('{submissionId}', 'An older Grimoire wrote this.', '{Noon:O}', 'done', '{runId}', NULL);

            INSERT INTO runs VALUES
                ('{runId}', '{submissionId}', '{Noon:O}',
                 '{string.Join('\n', ToolGrant.ForIngest)}', '{Noon:O}', NULL, NULL);
            """;

        command.ExecuteNonQuery();
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

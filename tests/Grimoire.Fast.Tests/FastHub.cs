using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Runs;
using Microsoft.Extensions.Time.Testing;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The hub as the Fast suite composes it: the real board, conductor and intake, with in-memory
/// adapters at the two owned ports and a clock a test moves itself.
/// </summary>
internal sealed class FastHub
{
    public FastHub()
        : this(new HubJournal())
    {
    }

    /// <summary>One journal, written to by both doubles, so that they share a timeline.</summary>
    private FastHub(HubJournal journal)
        : this(new InMemorySubmissionStore(journal), journal, new InMemoryRunRecord())
    {
    }

    /// <summary>
    /// A hub over a store and a record that already hold something — which is what a restart is. It
    /// runs the same start-up the composition root runs, so the order RUNS-006 asks for is the real
    /// one and not a copy of it (HubApplication.RestoreAfterAStop).
    /// </summary>
    /// <remarks>
    /// The record comes across the restart with the store because that is what it does on disk: the
    /// files in <c>runs/</c> outlive the process that wrote them, and a record the new hub could not
    /// see would hide whether the interrupted run's ending ever reached it (RUNS-007). It comes
    /// across <em>reopened</em> — what the last process kept in its head is gone with it, which is
    /// the line the adapter is cut on (<c>InMemoryRunRecord.Reopened</c>).
    /// </remarks>
    public FastHub(InMemorySubmissionStore store, HubJournal journal, InMemoryRunRecord record)
    {
        Store = store;
        Journal = journal;
        Record = record;
        Harness = new InMemoryAgentHarness(journal);
        Clock = FastSuite.Clock();

        // The same one the composition root builds, so a test reads the streams the browser reads —
        // in process, as an IAsyncEnumerable, with no socket anywhere (research.md R-11).
        Live = new LiveUpdates();

        Board = new RunBoard(Clock, store, queued => Live.Changed(
            queued is Question ? LiveUpdates.Chat : LiveUpdates.Submissions));

        // The same knot the composition root ties: a run that ends lets the next one start
        // (HubApplication.Build).
        RunQueue? queue = null;
        Conductor = new RunConductor(
            Board, Harness, Wiki, Record, Chat, Live, Clock, Model, () => queue!.PumpAsync());
        queue = new RunQueue(Board, Conductor, Harness, Prompt, QuestionPrompt);
        Queue = queue;
        Intake = new SubmissionIntake(Board, Queue);

        HubApplication.RestoreAfterAStop(store, Board, Harness, Record, Clock);

        // And then the queue is pumped, which is what the hub does once it is listening: a
        // submission that was waiting when Grimoire stopped starts by itself, with nobody
        // submitting anything (research.md R-03, the fourth of the four events). There is no
        // server here to wait for, so it happens straight after the restore.
        Queue.PumpAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// The hub told to stop, exactly as the composition root tells it
    /// (HubApplication.StopEverythingAsync): admission closed first, then what is under way
    /// stopped with it.
    /// </summary>
    public Task StopEverythingAsync() => HubApplication.StopEverythingAsync(Queue, Conductor);

    /// <summary>
    /// Grimoire stopped and started again over the same store. The clock starts afresh, as a new
    /// process's does.
    /// </summary>
    public FastHub Restarted() => new(Store, Journal, Record.Reopened());

    public const string Model = "claude-opus-4-5-20251101";

    public InMemorySubmissionStore Store { get; }

    /// <summary>What both doubles did, in the order they did it.</summary>
    public HubJournal Journal { get; }

    public FakeTimeProvider Clock { get; }

    public InMemoryAgentHarness Harness { get; }

    public InMemoryWikiStore Wiki { get; } = new();

    /// <summary>Where this hub's runs leave their records (RUNS-007).</summary>
    public InMemoryRunRecord Record { get; }

    /// <summary>What the browser would be sent (ACCESS-005, ACCESS-006, ACCESS-007).</summary>
    public LiveUpdates Live { get; }

    /// <summary>The one chat this hub holds (QUERY-005).</summary>
    public Chat Chat { get; } = new();

    public RunBoard Board { get; }

    public RunConductor Conductor { get; }

    public RunQueue Queue { get; }

    public SubmissionIntake Intake { get; }

    /// <summary>What the hub's instruction loader assembles, without a filesystem to read it from.</summary>
    public static string Prompt(string text, Guid runId) =>
        InstructionLoader.Payload("THE INSTRUCTION", "THE PURPOSE", text, runId);

    /// <summary>The same for a question's run, with the chat this hub holds (QUERY-002).</summary>
    public string QuestionPrompt(string question, Guid runId) =>
        InstructionLoader.QuestionPayload(
            "THE QUESTION INSTRUCTION",
            "THE PURPOSE",
            InstructionLoader.ConversationSoFar(Chat, runId),
            question,
            runId);

    public Task<SubmissionResult> SubmitAsync(string text, StartUpInputs? inputs = null) =>
        Intake.SubmitAsync(text, inputs ?? StartUpInputs.BothPresent);

    /// <summary>
    /// A failure acknowledged, the way the endpoint does it: the board is told, and the queue is
    /// asked either way (ACCESS-003, contracts/hub-http-api.md).
    /// </summary>
    public Task AcknowledgeAsync(Guid submissionId)
    {
        Board.Acknowledge(submissionId);
        return Queue.PumpAsync();
    }

    /// <summary>A submission that was accepted, with its run under way.</summary>
    public async Task<Submission> AcceptedAsync(string text = "A text.") =>
        (await SubmitAsync(text)).Accepted!;

    /// <summary>
    /// A question asked the way the chat's intake asks it: the board decides, and the queue is then
    /// asked for the next run (QUERY-001, RUNS-002).
    /// </summary>
    public async Task<QuestionResult> AskAsync(string text, StartUpInputs? inputs = null)
    {
        var result = Board.Ask(text, inputs ?? StartUpInputs.BothPresent);

        if (result.Accepted is { } question)
        {
            Chat.Ask(question);
            await Queue.PumpAsync().ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>A question that was accepted, with its run under way where nothing was ahead of it.</summary>
    public async Task<Question> AskedAsync(string text = "What does the wiki say about Ada Lovelace?") =>
        (await AskAsync(text)).Accepted!;
}

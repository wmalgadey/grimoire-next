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
        : this(new InMemorySubmissionStore(journal), journal)
    {
    }

    /// <summary>
    /// A hub over a store that already holds something — which is what a restart is. It runs the
    /// same start-up the composition root runs, so the order RUNS-006 asks for is the real one and
    /// not a copy of it (HubApplication.RestoreAfterAStop).
    /// </summary>
    public FastHub(InMemorySubmissionStore store, HubJournal journal)
    {
        Store = store;
        Journal = journal;
        Harness = new InMemoryAgentHarness(journal);
        Clock = FastSuite.Clock();
        Board = new SubmissionBoard(Clock, store);

        // The same knot the composition root ties: a run that ends lets the next one start
        // (HubApplication.Build).
        RunQueue? queue = null;
        Conductor = new RunConductor(Board, Harness, Wiki, Clock, () => queue!.PumpAsync());
        queue = new RunQueue(Board, Conductor, Harness, Prompt, Model);
        Queue = queue;
        Intake = new SubmissionIntake(Board, Queue);

        HubApplication.RestoreAfterAStop(store, Board, Harness);
    }

    /// <summary>
    /// Grimoire stopped and started again over the same store. The clock starts afresh, as a new
    /// process's does.
    /// </summary>
    public FastHub Restarted() => new(Store, Journal);

    public const string Model = "claude-opus-4-5-20251101";

    public InMemorySubmissionStore Store { get; }

    /// <summary>What both doubles did, in the order they did it.</summary>
    public HubJournal Journal { get; }

    public FakeTimeProvider Clock { get; }

    public InMemoryAgentHarness Harness { get; }

    public InMemoryWikiStore Wiki { get; } = new();

    public SubmissionBoard Board { get; }

    public RunConductor Conductor { get; }

    public RunQueue Queue { get; }

    public SubmissionIntake Intake { get; }

    /// <summary>What the hub's instruction loader assembles, without a filesystem to read it from.</summary>
    public static string Prompt(string text, Guid runId) =>
        InstructionLoader.Payload("THE INSTRUCTION", "THE PURPOSE", text, runId);

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
}

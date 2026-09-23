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
    {
        Clock = FastSuite.Clock();
        Board = new SubmissionBoard(Clock);

        // The same knot the composition root ties: a run that ends lets the next one start
        // (HubApplication.Build).
        RunQueue? queue = null;
        Conductor = new RunConductor(Board, Harness, Wiki, Clock, () => queue!.PumpAsync());
        queue = new RunQueue(Board, Conductor, Harness, Prompt, Model);
        Queue = queue;
        Intake = new SubmissionIntake(Board, Queue);
    }

    public const string Model = "claude-opus-4-5-20251101";

    public FakeTimeProvider Clock { get; }

    public InMemoryAgentHarness Harness { get; } = new();

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

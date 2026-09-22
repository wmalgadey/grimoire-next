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
        Conductor = new RunConductor(Board, Harness, Wiki, Clock);
        Intake = new SubmissionIntake(Board, Harness, Conductor, Prompt, Model);
    }

    public const string Model = "claude-opus-4-5-20251101";

    public FakeTimeProvider Clock { get; }

    public InMemoryAgentHarness Harness { get; } = new();

    public InMemoryWikiStore Wiki { get; } = new();

    public SubmissionBoard Board { get; }

    public RunConductor Conductor { get; }

    public SubmissionIntake Intake { get; }

    /// <summary>What the hub's instruction loader assembles, without a filesystem to read it from.</summary>
    public static string Prompt(string text, Guid runId) =>
        InstructionLoader.Payload("THE INSTRUCTION", "THE PURPOSE", text, runId);

    public Task<SubmissionResult> SubmitAsync(string text, StartUpInputs? inputs = null) =>
        Intake.SubmitAsync(text, inputs ?? StartUpInputs.BothPresent);

    /// <summary>A submission that was accepted, with its run under way.</summary>
    public async Task<Submission> AcceptedAsync(string text = "A text.") =>
        (await SubmitAsync(text)).Accepted!;
}

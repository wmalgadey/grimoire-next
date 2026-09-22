using Grimoire.Hub;

namespace Grimoire.Fast.Tests;

/// <summary>
/// What a run is given, and what does not reach it (INGEST-002, Constitution V.1).
/// </summary>
[Trait("level", "fast")]
[Trait("req", "INGEST-002")]
public sealed class DispatchPayloadTests
{
    private readonly FastHub hub = new();

    [Fact]
    public async Task Dispatch_CarriesTheInstructionThePurposeTheTextAndTheRunIdentifier()
    {
        await hub.AcceptedAsync("Ada Lovelace wrote the first program.");

        var prompt = hub.Harness.Dispatched.Single().Prompt;
        var runId = hub.Harness.Dispatched.Single().RunId;

        Assert.Contains("THE INSTRUCTION", prompt, StringComparison.Ordinal);
        Assert.Contains("THE PURPOSE", prompt, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace wrote the first program.", prompt, StringComparison.Ordinal);
        Assert.Contains(runId.ToString(), prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_CarriesNothingBeyondThoseFour()
    {
        await hub.AcceptedAsync("Ada Lovelace wrote the first program.");

        var dispatched = hub.Harness.Dispatched.Single();

        // Equality, not containment: anything else that had found its way into the prompt would
        // show up here. Only the instruction loader puts text into it.
        Assert.Equal(
            InstructionLoader.Payload(
                "THE INSTRUCTION",
                "THE PURPOSE",
                "Ada Lovelace wrote the first program.",
                dispatched.RunId),
            dispatched.Prompt);
    }

    [Fact]
    public async Task Dispatch_RunsOnThePinnedModelTheHubWasStartedWith()
    {
        await hub.AcceptedAsync();

        // A start-up input, not a per-submission choice, and an id rather than an alias: a run is
        // reproducible and its cost attributable only if the model is fixed (research.md R-11).
        Assert.Equal(FastHub.Model, hub.Harness.Dispatched.Single().Model);
    }

    [Fact]
    public async Task Dispatch_CarriesTheGrantTheRunRecorded()
    {
        var submission = await hub.AcceptedAsync();

        Assert.Equal(hub.Conductor.Of(submission.Id)!.Grant, hub.Harness.Dispatched.Single().Grant);
    }
}

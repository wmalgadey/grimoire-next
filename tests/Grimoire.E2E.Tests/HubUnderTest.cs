using Grimoire.Agent;
using Grimoire.Hub;
using Microsoft.AspNetCore.Builder;

namespace Grimoire.E2E.Tests;

/// <summary>
/// A run that starts and then says nothing more, which is all user story 1 needs of the agent:
/// the point of INGEST-001 is that the browser is free while the run goes on.
/// </summary>
/// <remarks>
/// An in-memory adapter at an owned port, like the Fast suite's (Constitution III.9). The real
/// adapter — <c>HarnessProcess</c>, driving the real <c>claude</c> CLI — arrives with T031, and
/// the Contract suite is what drives that.
/// </remarks>
internal sealed class DispatchOnlyHarness : IAgentHarness
{
    public Task DispatchAsync(AgentDispatch dispatch, RunReport report, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task NudgeAsync(Guid runId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(Guid runId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The hub, listening on a loopback port the operating system picks, with its two start-up inputs
/// in place. A real server for a real browser to talk to.
/// </summary>
internal sealed class HubUnderTest : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly string startUpInputs;

    private HubUnderTest(WebApplication app, string startUpInputs, string address)
    {
        this.app = app;
        this.startUpInputs = startUpInputs;
        Address = address;
    }

    public string Address { get; }

    public static async Task<HubUnderTest> StartAsync(CancellationToken cancellationToken)
    {
        // Both texts every run receives (V.1). Their content does not matter here — ACCESS-001 is
        // about the browser door; what a run is given is INGEST-002, proven a level down.
        var directory = Directory.CreateTempSubdirectory("grimoire-e2e-").FullName;
        var instruction = Path.Combine(directory, "ingest.md");
        var purpose = Path.Combine(directory, "purpose.md");
        await File.WriteAllTextAsync(instruction, "# Instruction", cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(purpose, "# Purpose", cancellationToken).ConfigureAwait(false);

        var app = HubApplication.Build(
            ["--urls", "http://127.0.0.1:0"],
            new HubOptions(instruction, purpose),
            new DispatchOnlyHarness(),
            TimeProvider.System);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        return new HubUnderTest(app, directory, app.Urls.First());
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(startUpInputs, recursive: true);
    }
}

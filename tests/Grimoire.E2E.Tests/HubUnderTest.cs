using System.Collections.Concurrent;
using System.Net.Http.Json;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;
using Grimoire.Wiki.Adapters;
using Microsoft.AspNetCore.Builder;

namespace Grimoire.E2E.Tests;

/// <summary>
/// A run that does nothing until the test says so: it reports in, or it ends, when it is told to.
/// That is all the browser needs of the agent — what the browser shows is a submission's state
/// (ACCESS-002), and driving a submission to each of the four states is how the states get there.
/// </summary>
/// <remarks>
/// An in-memory adapter at an owned port, like the Fast suite's (Constitution III.9). The real
/// adapter is <c>HarnessProcess</c>, driving the real <c>claude</c> CLI, and the Contract suite is
/// what drives that.
/// </remarks>
internal sealed class DrivableHarness : IAgentHarness
{
    /// <summary>
    /// Concurrent because a run is dispatched on the request's thread and driven on the test's.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, RunReport> reports = new();

    public Task DispatchAsync(AgentDispatch dispatch, RunReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        reports[dispatch.SubmissionId] = report;
        return Task.CompletedTask;
    }

    public Task NudgeAsync(Guid runId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(Guid runId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>What the CLI's <c>system/init</c> does to the run: submitted becomes running.</summary>
    public void ReportIn(Guid submissionId) => reports[submissionId].AgentReportedIn(submissionId);

    /// <summary>The run is over, one way or the other.</summary>
    public void End(Guid submissionId, RunOutcome outcome) => reports[submissionId].RunEnded(submissionId, outcome);
}

/// <summary>
/// The hub, listening on a loopback port the operating system picks, with its two start-up inputs
/// in place. A real server for a real browser to talk to.
/// </summary>
internal sealed class HubUnderTest : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly HttpClient client;
    private readonly string startUpInputs;

    private HubUnderTest(WebApplication app, DrivableHarness agent, string startUpInputs, string address)
    {
        this.app = app;
        this.startUpInputs = startUpInputs;
        Agent = agent;
        Address = address;
        client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public string Address { get; }

    /// <summary>The run the hub dispatched to, which a test drives from state to state.</summary>
    public DrivableHarness Agent { get; }

    public static async Task<HubUnderTest> StartAsync(CancellationToken cancellationToken)
    {
        // Both texts every run receives (V.1). Their content does not matter here — the browser
        // door is ACCESS-001 and ACCESS-002; what a run is given is INGEST-002, proven a level down.
        var directory = Directory.CreateTempSubdirectory("grimoire-e2e-").FullName;
        var instruction = Path.Combine(directory, "ingest.md");
        var purpose = Path.Combine(directory, "purpose.md");
        await File.WriteAllTextAsync(instruction, "# Instruction", cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(purpose, "# Purpose", cancellationToken).ConfigureAwait(false);

        var agent = new DrivableHarness();

        var app = HubApplication.Build(
            ["--urls", "http://127.0.0.1:0"],
            new HubOptions(instruction, purpose, WikiRoot: directory, Model: "claude-opus-4-5-20251101"),
            agent,
            new FileSystemWikiStore(directory),
            TimeProvider.System);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        return new HubUnderTest(app, agent, directory, app.Urls.First());
    }

    /// <summary>
    /// A text submitted the way the page submits it, answering with the accepted submission's id.
    /// A submission the browser is to show has to exist before the browser can show it; that the
    /// form itself submits is ACCESS-001, proven in <see cref="SubmitTextTests"/>.
    /// </summary>
    public async Task<Guid> SubmitAsync(string text, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/api/submissions", new { text }, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var accepted = await response.Content.ReadFromJsonAsync<SubmissionView>(cancellationToken)
            .ConfigureAwait(false);

        return Guid.Parse(accepted!.Id);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(startUpInputs, recursive: true);
    }
}

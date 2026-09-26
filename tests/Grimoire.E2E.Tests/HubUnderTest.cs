using System.Collections.Concurrent;
using System.Net.Http.Json;
using Grimoire.Agent;
using Grimoire.Hub;
using Grimoire.Hub.Api;
using Grimoire.Runs.Adapters;
using Grimoire.Wiki.Adapters;
using Microsoft.AspNetCore.Builder;

namespace Grimoire.E2E.Tests;

/// <summary>
/// A run that does nothing until the test says so: it reports in, or it ends, when it is told to.
/// That is all the browser needs of the agent — what the browser shows is a submission's state and,
/// where it has a run, that run's model and figures (ACCESS-005) and what it did (ACCESS-006).
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

    public Task NothingFurtherAsync(Guid runId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The agents this harness was asked to terminate at start-up (RUNS-006). Recorded and not
    /// acted on: there is no real process behind a drivable run.
    /// </summary>
    public List<AgentProcessIdentity> Terminated { get; } = [];

    public void Terminate(AgentProcessIdentity identity) => Terminated.Add(identity);

    /// <summary>What the CLI's <c>system/init</c> does to the run: submitted becomes running.</summary>
    public void ReportIn(Guid submissionId) => Of(submissionId).AgentReportedIn(submissionId);

    /// <summary>The run is over, one way or the other, and for the reason the record's tail names.</summary>
    public void End(
        Guid submissionId,
        RunOutcome outcome,
        RunEndedBecause because = RunEndedBecause.StoppedWithItsLogEntry) =>
        Of(submissionId).RunEnded(submissionId, outcome, because);

    /// <summary>What the streamed usage of a turn does (GUARD-004, RUNS-010).</summary>
    public void Spend(Guid submissionId, long tokensUsed) =>
        Of(submissionId).CostSoFar(
            submissionId, tokensUsed, new Dictionary<string, ModelTokens>(StringComparer.Ordinal));

    /// <summary>One thing the run did (RUNS-009).</summary>
    public void Did(Guid submissionId, TranscriptMoment moment) =>
        Of(submissionId).MomentHappened(submissionId, moment);

    /// <summary>A tool call, which is also what raises the run's call count (RUNS-010).</summary>
    public void Called(Guid submissionId, string tool, string arguments) =>
        Did(submissionId, new TranscriptMoment(RunMomentKind.ToolCalled, tool, arguments));

    /// <summary>What that call returned, whole (RUNS-009).</summary>
    public void Returned(Guid submissionId, string tool, string result) =>
        Did(submissionId, new TranscriptMoment(RunMomentKind.ToolReturned, tool, result));

    /// <summary>The agent's own text between the calls (RUNS-009).</summary>
    public void Said(Guid submissionId, string text) =>
        Did(submissionId, new TranscriptMoment(RunMomentKind.AgentSaid, Tool: null, text));

    /// <summary>The run's process is gone, with this exit code. Where a run ends.</summary>
    public void Exit(Guid submissionId, int exitCode) => Of(submissionId).AgentExited(submissionId, exitCode);

    /// <summary>
    /// The run for this submission, waited for rather than assumed.
    /// </summary>
    /// <remarks>
    /// A submission does not start its run at the moment it is accepted: it starts when the queue
    /// reaches it, which for a text waiting behind a failure is after the acknowledgement the
    /// browser sent, on the hub's own thread and after the response was written (RUNS-002,
    /// RUNS-003). A test drives the agent from outside, so it waits for the run the way the page's
    /// own polling waits for the state.
    /// </remarks>
    private RunReport Of(Guid submissionId)
    {
        var giveUpAt = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!reports.TryGetValue(submissionId, out var report))
        {
            if (DateTime.UtcNow > giveUpAt)
            {
                throw new InvalidOperationException($"no run was ever dispatched for submission {submissionId}");
            }

            Thread.Sleep(20);
        }

        return reports[submissionId];
    }
}

/// <summary>
/// The hub, listening on a loopback port the operating system picks, with its two start-up inputs
/// in place. A real server for a real browser to talk to.
/// </summary>
internal sealed class HubUnderTest : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly HttpClient client;
    private readonly string directory;

    /// <summary>
    /// Whether this hub is the one that made the directory. A hub started again over another's
    /// state shares it and must not take it away underneath it.
    /// </summary>
    private readonly bool ownsTheDirectory;

    private HubUnderTest(WebApplication app, DrivableHarness agent, string directory, string address, bool ownsTheDirectory)
    {
        this.app = app;
        this.directory = directory;
        this.ownsTheDirectory = ownsTheDirectory;
        Agent = agent;
        Address = address;
        client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public string Address { get; }

    /// <summary>
    /// Where Grimoire keeps its own bookkeeping — the submissions and the records — and the wiki it
    /// writes into. Siblings, never one inside the other (contracts/submission-store.md).
    /// </summary>
    public string StateDirectory => Path.Combine(directory, "state");

    public string WikiDirectory => Path.Combine(directory, "wiki");

    /// <summary>The run the hub dispatched to, which a test drives from state to state.</summary>
    public DrivableHarness Agent { get; }

    public static Task<HubUnderTest> StartAsync(CancellationToken cancellationToken) =>
        StartAsync(Directory.CreateTempSubdirectory("grimoire-e2e-").FullName, ownsTheDirectory: true, cancellationToken);

    /// <summary>
    /// Grimoire stopped and started again over the same state, which is what a restart is — a
    /// second process reading what the first one left (RUNS-004). A new agent adapter comes with
    /// it, as a new process's does: nothing of the first hub is shared but the directory.
    /// </summary>
    public static async Task<HubUnderTest> RestartedAsync(HubUnderTest stopped, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopped);

        await stopped.StopAsync(cancellationToken).ConfigureAwait(false);

        return await StartAsync(stopped.directory, ownsTheDirectory: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The hub goes down, and its directory stays where it is.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => app.StopAsync(cancellationToken);

    private static async Task<HubUnderTest> StartAsync(
        string directory, bool ownsTheDirectory, CancellationToken cancellationToken)
    {
        // Both texts every run receives (V.1). Their content does not matter here — the browser
        // door is ACCESS-001, ACCESS-005 and ACCESS-006; what a run is given is INGEST-002, proven a
        // level down.
        //
        // The wiki and the queue are siblings, never one inside the other: the wiki store lists
        // every non-hidden file it finds, so a queue kept inside the wiki would be served to the
        // agent as a page (contracts/submission-store.md). The hub refuses that arrangement at
        // start-up, and a fixture that used it would be testing something the product forbids.
        var wiki = Path.Combine(directory, "wiki");
        var state = Path.Combine(directory, "state");
        Directory.CreateDirectory(wiki);
        var instruction = Path.Combine(directory, "ingest.md");
        var purpose = Path.Combine(directory, "purpose.md");
        await File.WriteAllTextAsync(instruction, "# Instruction", cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(purpose, "# Purpose", cancellationToken).ConfigureAwait(false);

        var agent = new DrivableHarness();

        var app = HubApplication.Build(
            ["--urls", "http://127.0.0.1:0"],
            new HubOptions(instruction, purpose, WikiRoot: wiki, Model: "claude-opus-4-5-20251101"),
            agent,
            new FileSystemWikiStore(wiki),
            new SqliteSubmissionStore(state),
            new MarkdownRunRecord(state),
            TimeProvider.System);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        return new HubUnderTest(app, agent, directory, app.Urls.First(), ownsTheDirectory);
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

    /// <summary>
    /// A failure acknowledged the way the page acknowledges it, so that the queue moves on
    /// (ACCESS-003, RUNS-003). That a person can reach this from the browser is
    /// <see cref="AcknowledgementTests"/>; here it is a step on the way to somewhere else.
    /// </summary>
    public async Task AcknowledgeAsync(Guid submission, CancellationToken cancellationToken)
    {
        var response = await client
            .PostAsync(new Uri($"/api/submissions/{submission}/acknowledgement", UriKind.Relative), null, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync().ConfigureAwait(false);

        if (ownsTheDirectory)
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

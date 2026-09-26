using System.Net.Http.Json;
using Grimoire.Hub;
using Grimoire.Hub.Api;
using Grimoire.Runs;
using Microsoft.AspNetCore.Builder;

namespace Grimoire.Fast.Tests;

/// <summary>
/// The hub as a running server, built through <c>HubApplication.Build</c> with in-memory adapters at
/// every owned port — the same application the composition root builds (Constitution III.9).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FastHub"/> is what almost every test here uses: it composes the board, the conductor and
/// the intake directly, with no server and no files, which is what keeps the suite inside its 15 s.
/// This one exists for the one thing that needs a request to reach an endpoint at all — the record
/// endpoint of ACCESS-006 — and it is in the Fast suite because nothing outside the process is
/// involved: loopback, in-memory adapters, and a clock a test moves itself.
/// </para>
/// <para>
/// The two start-up inputs are real files, because <c>InstructionLoader</c> reads paths and is not
/// behind a port. Their content does not matter here; what a run is given is INGEST-002, proven
/// without a server.
/// </para>
/// </remarks>
internal sealed class HostedHub : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly HttpClient client;
    private readonly string directory;

    public HostedHub(bool recordEverythingFails = false)
    {
        directory = Directory.CreateTempSubdirectory("grimoire-fast-hub-").FullName;

        var instruction = Path.Combine(directory, "ingest.md");
        var purpose = Path.Combine(directory, "purpose.md");
        File.WriteAllText(instruction, "# Instruction");
        File.WriteAllText(purpose, "# Purpose");

        Record.FailWrites = recordEverythingFails;

        app = HubApplication.Build(
            ["--urls", "http://127.0.0.1:0"],
            new HubOptions(instruction, purpose, WikiRoot: directory, Model: FastHub.Model),
            Agent,
            Wiki,
            Store,
            Record,
            FastSuite.Clock());

        app.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        Runs = new RunReference(Store);
    }

    public InMemoryAgentHarness Agent { get; } = new();

    public InMemoryWikiStore Wiki { get; } = new();

    public InMemorySubmissionStore Store { get; } = new();

    public InMemoryRunRecord Record { get; } = new();

    /// <summary>
    /// The runs, read back out of the store. The composition root does not hand the conductor out, and
    /// it does not have to: which run a submission was given is a fact the store holds.
    /// </summary>
    public RunReference Runs { get; }

    /// <summary>
    /// A text submitted the way the page submits it, answering with the accepted submission's id. The
    /// intake awaits the pump, so the run is dispatched by the time this returns.
    /// </summary>
    public async Task<Guid> SubmitAsync(string text)
    {
        var response = await client
            .PostAsJsonAsync("/api/submissions", new { text }, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var accepted = await response.Content
            .ReadFromJsonAsync<SubmissionView>(TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return Guid.Parse(accepted!.Id);
    }

    public Task<HttpResponseMessage> GetAsync(string path) =>
        client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(directory, recursive: true);
    }
}

/// <summary>Which run a submission was given, as the store holds it.</summary>
internal sealed class RunReference(InMemorySubmissionStore store)
{
    public StoredRun? Of(Guid submissionId) =>
        store.Load().FirstOrDefault(s => s.Id == submissionId)?.Run;
}

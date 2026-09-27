using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
        var questionInstruction = Path.Combine(directory, "question.md");
        var purpose = Path.Combine(directory, "purpose.md");
        File.WriteAllText(instruction, "# Instruction");
        File.WriteAllText(questionInstruction, "# Question");
        File.WriteAllText(purpose, "# Purpose");

        Record.FailWrites = recordEverythingFails;

        app = HubApplication.Build(
            ["--urls", "http://127.0.0.1:0"],
            new HubOptions(instruction, questionInstruction, purpose, WikiRoot: directory, Model: FastHub.Model),
            Agent,
            Wiki,
            Store,
            Record,
            FastSuite.Clock());

        app.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        Runs = new RunReference(Store);
    }

    /// <summary>
    /// The three start-up inputs are real files, because <c>InstructionLoader</c> reads paths and is not
    /// behind a port. Deleting one is how a test reaches INGEST-003's and QUERY-003's "not at the path
    /// the hub was started with", which is read per submission and per question rather than once.
    /// </summary>
    public void IngestInstructionIsGone() => File.Delete(Path.Combine(directory, "ingest.md"));

    public void QuestionInstructionIsGone() => File.Delete(Path.Combine(directory, "question.md"));

    public void PurposeDescriptionIsGone() => File.Delete(Path.Combine(directory, "purpose.md"));

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

    /// <summary>
    /// A command the page sends with no body — the acknowledgement is the one there is.
    /// </summary>
    public Task<HttpResponseMessage> PostAsync(string path) =>
        client.PostAsync(new Uri(path, UriKind.Relative), content: null, TestContext.Current.CancellationToken);

    /// <summary>
    /// A command the page sends with a JSON body, the way the page sends it.
    /// </summary>
    /// <remarks>
    /// It exists so that the status and the wire names an endpoint answers with are read from the
    /// endpoint rather than from the objects behind it. Those names are what the browser reads: a test
    /// that asserted the enum the board returns would pass while the page was told something else — the
    /// same shape of mistake that let a hub serving five tools at the question door pass its tests
    /// (GUARD-005's history).
    /// </remarks>
    public Task<HttpResponseMessage> PostAsync<TBody>(string path, TBody body) =>
        client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, TestContext.Current.CancellationToken);

    /// <summary>
    /// One of the hub's streams, opened the way the browser's <c>EventSource</c> opens it.
    /// </summary>
    public Task<EventStream> WatchAsync(string path) => EventStream.OpeningAsync(client, path);

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

/// <summary>
/// One <c>text/event-stream</c>, read event by event as a test asks for the next one (ACCESS-005,
/// ACCESS-006, ACCESS-007).
/// </summary>
/// <remarks>
/// <para>
/// A reader of its own rather than an assertion on a whole body: a stream never ends, so a test that
/// read it to completion would wait for ever. This one opens the response as soon as the headers are
/// there and hands over each event as it arrives, which is also the order a test needs — subscribe,
/// then make something happen, then read what was sent.
/// </para>
/// <para>
/// That <c>TypedResults.ServerSentEvents</c> frames an event is framework behaviour and is not
/// tested (Constitution III.8, research.md R-01). What is read here is the event name and its one
/// line of JSON, because that is what we put on the stream.
/// </para>
/// </remarks>
internal sealed class EventStream(HttpResponseMessage response, StreamReader lines) : IAsyncDisposable
{
    /// <summary>
    /// How long a test waits for an event that should already be on its way. Generous enough not to
    /// be flaky on a loaded machine and far inside the suite's own 15 s (Constitution III.7): a test
    /// that reaches it has found a stream that sends nothing, which is a failure and not a slow pass.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    public static async Task<EventStream> OpeningAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(
            new Uri(path, UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var stream = await response.Content
            .ReadAsStreamAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        return new EventStream(response, new StreamReader(stream, Encoding.UTF8));
    }

    /// <summary>The next event: its name, and its <c>data</c> as the one line of JSON it is.</summary>
    public async Task<(string Event, string Data)> NextAsync()
    {
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        patience.CancelAfter(Patience);

        var name = string.Empty;
        var data = new StringBuilder();

        while (await lines.ReadLineAsync(patience.Token).ConfigureAwait(false) is { } line)
        {
            // A blank line ends the event, which is the whole of the framing this reader knows.
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    return (name, data.ToString());
                }

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                // One line of JSON per event, by this API's own rule, so several `data:` lines would
                // be a shape the contract does not promise — joined with the newline SSE would put
                // back, so a test that met one would see it rather than a silently mangled body.
                data.Append(data.Length > 0 ? "\n" : string.Empty).Append(line["data:".Length..].TrimStart());
            }
        }

        throw new InvalidOperationException("the stream ended without another event");
    }

    /// <summary>The next event's <c>data</c>, read as the shape the contract promises.</summary>
    public async Task<T> NextAsync<T>(string expected)
    {
        var (name, data) = await NextAsync().ConfigureAwait(false);

        Assert.Equal(expected, name);

        return JsonSerializer.Deserialize<T>(data)!;
    }

    public async ValueTask DisposeAsync()
    {
        lines.Dispose();
        response.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

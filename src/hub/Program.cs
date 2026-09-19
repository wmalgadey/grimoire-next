using System.Text.Json;
using Grimoire.Dispatch;
using Grimoire.Ingest.Adapters;
using Grimoire.Hub;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki;
using Grimoire.Wiki.Adapters;
using Microsoft.Extensions.Logging.Console;

// The composition root (ADR-0011). Everything the hub is made of is wired here and nowhere else:
// configuration straight from the environment with no abstraction over it, structured JSON logs
// on stdout, the two stores, the run queue, and the endpoint map (constitution VII.1).

var builder = WebApplication.CreateBuilder(args);

// Configuration is environment variables only, read from the process environment and nothing
// else — not the host's aggregate configuration, which would also take command-line arguments and
// settings files and put a token in argv (contracts/deployment.md "Environment contract"). It is
// registered rather than read here so an in-process test host can give each hub its own,
// parsed by the same rules, without touching the process environment every hub shares. A missing
// required variable still fails fast and loudly, before the replica serves anything
// (contracts/deployment.md "Startup", step 1): see the first line after Build().
builder.Services.AddSingleton(_ => HubConfiguration.FromEnvironment());

// Structured JSON on stdout, one event per line. No files, no rotation, no sink configuration:
// the container convention, and the transport for every signal in the plan's observability table.
// GRIMOIRE_LOG_FORMAT=text swaps the formatter for reading by eye; the events are the same.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "HH:mm:ss.fff ";
});
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});
builder.Services.AddOptions<ConsoleLoggerOptions>().Configure<HubConfiguration>((options, configuration) =>
    options.FormatterName = configuration.LogFormat == LogFormat.Text
        ? ConsoleFormatterNames.Simple
        : ConsoleFormatterNames.Json);

// The two stores, opened once. The wiki repository holds content and history; the operational
// store holds the task artifact and commit identities, never wiki content (data-model).
builder.Services.AddSingleton(services =>
{
    var store = new SqliteStore(services.GetRequiredService<HubConfiguration>().StateDatabasePath);
    store.EnsureSchema();
    return store;
});
builder.Services.AddSingleton(services =>
    new GitCli(services.GetRequiredService<HubConfiguration>().WikiRepositoryPath));

// One client for the process lifetime: FetchProxy never changes after startup, and a fresh
// HttpClient (and its handler and sockets) per submission is a resource leak under sustained URL
// ingestion (UrlFetch is not disposable, and nothing was disposing it either).
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<HubConfiguration>();
    return UrlFetch.Create(configuration.FetchProxy, TimeSpan.FromMilliseconds(configuration.FetchDeadlineMs));
});

// The single wiki mutation path, and the single-writer lock inside it (constitution II.1).
builder.Services.AddSingleton(services => new WikiMutation(
    services.GetRequiredService<GitCli>(), services.GetRequiredService<ILogger<WikiMutation>>()));
builder.Services.AddSingleton(services => new RunOutcomeHandler(services.GetRequiredService<WikiMutation>()));

builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<HubConfiguration>();
    return new DispatchSettings(
        RepositoryRoot: configuration.ApplicationRoot ?? RepositoryRoot(),
        WikiRepositoryPath: configuration.WikiRepositoryPath,
        ModelBaseUrl: configuration.ModelBaseUrl,
        ModelToken: configuration.ModelToken,
        InstructionPath: configuration.InstructionPath,
        Limit: new RunLimit(configuration.RunMaxToolCalls, configuration.RunMaxElapsedMs));
});

builder.Services.AddSingleton<Dispatcher>();
builder.Services.AddSingleton<RunQueue>();
builder.Services.AddSingleton<StartupRecovery>();
builder.Services.AddSingleton<GracefulShutdown>();

// The drain has to outlast the runner it is terminating, or the host would abandon the sequence
// half-done and leave exactly the state SIGTERM exists to avoid (contracts/deployment.md
// "SIGTERM"). The default five seconds is shorter than the grace the shutdown itself allows.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddOperations();
builder.Services.AddOpenApi();

var app = builder.Build();

// Step 1 of startup: the configuration is read, and a replica that cannot work fails here, naming
// every missing variable at once, before anything else is resolved or served.
_ = app.Services.GetRequiredService<HubConfiguration>();

// The built frontend is served from this same origin, so the surfaces and /api need no CORS and
// no second listener (ADR-0002, contracts/deployment.md Topology).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapTaskEndpoints();
app.MapOperations();

// SvelteKit is built as an SPA (adapter-static, `fallback: "index.html"`): client-side routing
// owns every path under it, so a direct navigation or reload of e.g. /tasks/{taskId} has to reach
// the same document `/` serves, at that same URL. An unmatched /api path is a real 404, not the
// app shell — the more specific pattern wins the fallback routing.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

// Startup, steps 3 and 4 (contracts/deployment.md "Lifecycle"), in that order and before the
// listener opens: whatever the previous process left running is failed, the working tree is reset,
// and only then is the backlog dispatched. Awaited rather than backgrounded — step 5 is "/readyz
// starts reporting ready", and a replica that answered ready while a dirty tree was still being
// reset would be lying about the one thing readiness is for.
await app.Services.GetRequiredService<StartupRecovery>().Recover(CancellationToken.None);

// SIGTERM. ApplicationStopping runs while the server is still answering, which is what lets
// /readyz report draining long enough for traffic to leave (step 1). Blocking here is deliberate:
// the host must not proceed to close the listener until the run is settled and the tree is reset.
app.Lifetime.ApplicationStopping.Register(() =>
{
    // Step 1 is here rather than inside GracefulShutdown because readiness is this slice's
    // surface and the shutdown is the dispatch slice's; the order between them is wiring, and
    // wiring lives in the composition root.
    app.Services.GetRequiredService<DrainState>().BeginDraining();
    app.Services.GetRequiredService<GracefulShutdown>().Drain(CancellationToken.None)
        .GetAwaiter().GetResult();
});

app.Run();

// Where src/agentrun/dist/main.js is resolved from. The hub runs from its own output directory in
// development and from the image's install root in a container; both sit under the repository or
// image root the runner build was published into.
static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "src", "agentrun")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

namespace Grimoire.Hub
{
    /// <summary>
    /// Anchors this assembly so <c>Microsoft.AspNetCore.Mvc.Testing</c> can host the hub — named
    /// rather than the global <c>Program</c>, which two top-level-statement apps would collide on
    /// in one test process.
    /// </summary>
    public sealed class HubEntryPoint;
}

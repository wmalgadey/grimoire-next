using System.Text.Json;
using Grimoire.Dispatch;
using Grimoire.Hub;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki;
using Grimoire.Wiki.Adapters;

// The composition root (ADR-0011). Everything the hub is made of is wired here and nowhere else:
// configuration straight from the environment with no abstraction over it, structured JSON logs
// on stdout, the two stores, the run queue, and the endpoint map (constitution VII.1).

var builder = WebApplication.CreateBuilder(args);

// Configuration is environment variables only. A missing required variable fails fast and loudly,
// before the replica serves anything (contracts/deployment.md "Startup", step 1).
var configuration = HubConfiguration.FromEnvironment();
builder.Services.AddSingleton(configuration);

// Structured JSON on stdout, one event per line. No files, no rotation, no sink configuration:
// the container convention, and the transport for every signal in the plan's observability table.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

// The two stores, opened once. The wiki repository holds content and history; the operational
// store holds the task artifact and commit identities, never wiki content (data-model).
builder.Services.AddSingleton(_ =>
{
    var store = new SqliteStore(configuration.StateDatabasePath);
    store.EnsureSchema();
    return store;
});
builder.Services.AddSingleton(_ => new GitCli(configuration.WikiRepositoryPath));

// The single wiki mutation path, and the single-writer lock inside it (constitution II.1).
builder.Services.AddSingleton(services => new WikiMutation(services.GetRequiredService<GitCli>()));
builder.Services.AddSingleton(services => new RunOutcomeHandler(services.GetRequiredService<WikiMutation>()));

builder.Services.AddSingleton(new DispatchSettings(
    RepositoryRoot: RepositoryRoot(),
    WikiRepositoryPath: configuration.WikiRepositoryPath,
    ModelBaseUrl: configuration.ModelBaseUrl,
    ModelToken: configuration.ModelToken,
    InstructionPath: configuration.InstructionPath,
    Limit: new RunLimit(configuration.RunMaxToolCalls, configuration.RunMaxElapsedMs)));

builder.Services.AddSingleton<Dispatcher>();
builder.Services.AddSingleton<RunQueue>();

builder.Services.AddOperations(configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

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

app.Run();

// Where src/agentrun/dist/main.js is resolved from. The hub runs from its own output directory in
// development and from the image's install root in a container; both sit under the repository or
// image root the runner build was published into.
static string RepositoryRoot()
{
    var fromEnvironment = Environment.GetEnvironmentVariable("GRIMOIRE_ROOT");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment;
    }

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

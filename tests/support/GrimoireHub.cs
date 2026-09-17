using Grimoire.Hub;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Grimoire.Tests.Support;

/// <summary>
/// The hub booted exactly as production boots it — the production composition root, real Kestrel,
/// a real git repository, a real SQLite file, real child processes — with the model endpoint
/// pointed at the scripted double (ADR-0004). Every observability assertion goes through this, so
/// a signal that only exists in a test wiring cannot pass (quality gate 6).
/// </summary>
public sealed class GrimoireHub : IDisposable
{
    private readonly WebApplicationFactory<HubEntryPoint> _factory;
    private readonly Dictionary<string, string?> _restore = [];

    private GrimoireHub(
        WebApplicationFactory<HubEntryPoint> factory,
        WikiRepositoryFixture wiki,
        ScriptedModelFixture? model,
        string stateDatabasePath,
        Dictionary<string, string?> restore)
    {
        _factory = factory;
        _restore = restore;
        Wiki = wiki;
        Model = model;
        StateDatabasePath = stateDatabasePath;
        Client = factory.CreateClient();
    }

    /// <summary>The wiki this hub commits into.</summary>
    public WikiRepositoryFixture Wiki { get; }

    /// <summary>The scripted model this hub's runs reach, when the suite started one.</summary>
    public ScriptedModelFixture? Model { get; }

    /// <summary>The operational store, so a second hub instance can share it across a restart.</summary>
    public string StateDatabasePath { get; }

    /// <summary>An HTTP client against the hub's own surface.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Boots a hub. Every configuration value is what production reads from the environment
    /// (contracts/deployment.md "Environment contract").
    /// </summary>
    public static GrimoireHub Start(
        WikiRepositoryFixture wiki,
        ScriptedModelFixture? model = null,
        string? stateDatabasePath = null,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null)
    {
        var statePath = stateDatabasePath
            ?? Path.Combine(Path.GetTempPath(), $"grimoire-state-{Guid.NewGuid():N}.db");

        var environment = new Dictionary<string, string?>
        {
            ["GRIMOIRE_WIKI_REPO"] = wiki.Path,
            ["GRIMOIRE_STATE_DB"] = statePath,
            // Port 1 is never listening, so a suite that needs a broken egress path gets one.
            ["GRIMOIRE_MODEL_BASE_URL"] = model?.BaseUrl ?? "http://127.0.0.1:1",
            ["GRIMOIRE_MODEL_TOKEN"] = "an-opaque-internal-token",
            ["GRIMOIRE_INSTRUCTION"] =
                Path.Combine(ScriptedModelFixture.RepositoryRoot, "src", "instructions", "ingest.md"),
        };

        foreach (var (name, value) in extraEnvironment ?? new Dictionary<string, string?>())
        {
            environment[name] = value;
        }

        var restore = new Dictionary<string, string?>();
        foreach (var (name, value) in environment)
        {
            restore[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        var factory = new WebApplicationFactory<HubEntryPoint>()
            .WithWebHostBuilder(builder => builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Production"));

        return new GrimoireHub(factory, wiki, model, statePath, restore);
    }

    /// <summary>Submits a source and returns the created task, or the problem document.</summary>
    public async Task<HttpResponseMessage> Submit(string kind, string value, CancellationToken cancellationToken) =>
        await Client.PostAsJsonAsync("/api/tasks", new { kind, value }, cancellationToken);

    /// <summary>Submits pasted text and returns the created task's identifier.</summary>
    public async Task<string> SubmitText(string value, CancellationToken cancellationToken)
    {
        var response = await Submit("text", value, cancellationToken);
        response.EnsureSuccessStatusCode();
        var task = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return task.GetProperty("id").GetString()!;
    }

    /// <summary>Opens a task in whatever state it is in (FR-020).</summary>
    public async Task<JsonElement> GetTask(string taskId, CancellationToken cancellationToken) =>
        await Client.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}", cancellationToken);

    /// <summary>
    /// Waits until a task reaches a terminal state. There is no live update on the surface
    /// (FR-018), so this is the suite's own polling, not the product's.
    /// </summary>
    public async Task<JsonElement> WaitForEnd(
        string taskId,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        JsonElement task = default;

        while (DateTime.UtcNow < deadline)
        {
            task = await GetTask(taskId, cancellationToken);
            var state = task.GetProperty("state").GetString();
            if (state is "completed" or "failed" or "reverted")
            {
                return task;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(
            $"Task {taskId} was still '{task.GetProperty("state").GetString()}' after the timeout.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();

        foreach (var (name, value) in _restore)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = StateDatabasePath + suffix;
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // A leftover temp file is not worth failing a test over.
                }
            }
        }
    }
}

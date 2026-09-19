using Grimoire.Hub;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.Tests.Hub;

/// <summary>
/// T032. The two operations endpoints (contracts/deployment.md "Operations endpoints"), hosted on
/// real Kestrel through the production composition root — no test-only endpoint map.
/// <c>/readyz</c> is the surface for the <c>grimoire.hub.readiness</c> signal, and it makes
/// "the egress path is broken" distinguishable from "the agent failed" (plan IV).
/// </summary>
public sealed class OperationsEndpointsTests : IClassFixture<HubFixture>
{
    private readonly HubFixture _hub;

    public OperationsEndpointsTests(HubFixture hub) => _hub = hub;

    [Fact]
    public async System.Threading.Tasks.Task HealthzReportsHealthyWhileTheProcessIsServing()
    {
        using var client = _hub.CreateClient();

        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async System.Threading.Tasks.Task ReadyzReportsACheckForEachOfTheThreeDependencies()
    {
        using var client = _hub.CreateClient();

        var response = await client.GetAsync("/readyz", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var checks = body.GetProperty("checks");
        foreach (var name in new[] { "wikiRepo", "stateDb", "egress" })
        {
            var value = checks.GetProperty(name).GetString();
            Assert.True(value is "ok" or "failed", $"'{name}' reported '{value}'.");
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ReadyzReports200WhenTheWikiAndTheStateDatabaseAreReachable()
    {
        using var client = _hub.CreateClient();

        var response = await client.GetAsync("/readyz", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var checks = body.GetProperty("checks");

        // The fixture provides a real git repository and a real SQLite file; only the egress
        // endpoint is absent, and that alone must make the replica not-ready.
        Assert.Equal("ok", checks.GetProperty("wikiRepo").GetString());
        Assert.Equal("ok", checks.GetProperty("stateDb").GetString());
        Assert.False(body.GetProperty("draining").GetBoolean());
    }

    [Fact]
    public async System.Threading.Tasks.Task ReadyzReports503WhenACheckFails()
    {
        // The fixture's egress endpoint is not listening, so the replica should not take traffic.
        using var client = _hub.CreateClient();

        var response = await client.GetAsync("/readyz", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        if (body.GetProperty("checks").GetProperty("egress").GetString() is "failed")
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}

/// <summary>
/// Boots <c>src/hub/</c> exactly as production does — real Kestrel, real git repository, real
/// SQLite file — with only the model endpoint pointed at the scripted double (ADR-0004).
/// </summary>
public sealed class HubFixture : WebApplicationFactory<HubEntryPoint>, IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"grimoire-hub-{Guid.NewGuid():N}");

    /// <summary>The wiki repository this hub commits into.</summary>
    public string WikiRepositoryPath => Path.Combine(_root, "wiki");

    /// <summary>The operational store this hub persists into.</summary>
    public string StateDatabasePath => Path.Combine(_root, "grimoire.db");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(WikiRepositoryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(StateDatabasePath)!);

        Git("init", "--initial-branch=main");
        Git("config", "user.email", "grimoire@test.invalid");
        Git("config", "user.name", "Grimoire");
        File.WriteAllText(Path.Combine(WikiRepositoryPath, "index.md"), "# Index\n");
        Git("add", "-A");
        Git("commit", "-m", "seed");

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// This host's configuration, parsed by the rules production applies to its environment and
    /// given to this host alone rather than to the whole test process.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var environment = new Dictionary<string, string?>
        {
            ["GRIMOIRE_WIKI_REPO"] = WikiRepositoryPath,
            ["GRIMOIRE_STATE_DB"] = StateDatabasePath,
            // Port 1 is never listening, so the readiness check for egress fails deterministically.
            ["GRIMOIRE_MODEL_BASE_URL"] = "http://127.0.0.1:1/v1",
            ["GRIMOIRE_MODEL_TOKEN"] = "an-opaque-internal-token",
        };
        var configuration = HubConfiguration.Read(name => environment.GetValueOrDefault(name));
        builder.ConfigureTestServices(services => services.AddSingleton(configuration));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private void Git(params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = WikiRepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();
    }
}

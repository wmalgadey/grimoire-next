using System.Text.Json;
using Grimoire.Tests.Support;
using static Grimoire.Tests.Deployment.ContainerRuntime;

namespace Grimoire.Tests.Deployment;

/// <summary>
/// The three images TS-18 runs, built once per suite from this working tree: <c>grimoire-hub</c>
/// and <c>grimoire-egress</c> from <c>deploy/</c> exactly as shipped, and the scripted model
/// (ADR-0004) standing where the model upstream stands.
/// </summary>
public sealed class BuiltImages : IAsyncLifetime
{
    public const string Hub = "grimoire-hub:ts18";
    public const string Egress = "grimoire-egress:ts18";
    public const string Model = "grimoire-scripted-model:ts18";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        if (!Available())
        {
            return;
        }

        var root = ScriptedModelFixture.RepositoryRoot;
        var build = TimeSpan.FromMinutes(20);
        await Must(["build", "-f", Path.Combine(root, "deploy", "hub.Dockerfile"), "-t", Hub, root], build);
        await Must(["build", "-f", Path.Combine(root, "deploy", "egress.Dockerfile"), "-t", Egress, root], build);

        var model = Path.Combine(root, "tests", "scripted-model");
        if (!File.Exists(Path.Combine(model, "dist", "server.js")))
        {
            throw new InvalidOperationException(
                "The scripted model is not built. Run `npm --prefix tests/scripted-model run build`.");
        }

        await Must(["build", "-f", Path.Combine(model, "Dockerfile"), "-t", Model, model], build);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // With no runtime the tests skip, each naming the reason; there is nothing to build here.
    private static bool Available() => UnavailableBecause is null;
}

/// <summary>
/// One deployment, stood up for one test: the hub on an internal network with no route off the
/// host, the egress proxy on that network and an outside one, and the scripted model on the outside
/// one only — so the hub's one reachable destination is the proxy, as in <c>deploy/compose.yaml</c>.
/// </summary>
/// <remarks>
/// Assembled with the container CLI rather than by running compose, with the same run-time flags
/// the compose file sets on the hub: read-only root, a <c>tmpfs</c> for <c>/tmp</c>, every
/// capability dropped, no privilege escalation. The images are the shipped ones, unmodified.
/// The hub is reached through <c>docker exec</c>: it is on a network that publishes nothing.
/// </remarks>
public sealed class ContainerDeployment : IAsyncDisposable
{
    public const string InternalToken = "ts18-opaque-internal-token";
    public const string UpstreamCredential = "sk-ant-ts18-upstream-credential-only-the-proxy-holds";

    private readonly string _id = Guid.NewGuid().ToString("N")[..10];
    private readonly List<string> _containers = [];
    private readonly List<string> _networks = [];
    private readonly List<string> _volumes = [];

    private ContainerDeployment()
    {
    }

    /// <summary>The hub container's name.</summary>
    public string HubContainer => $"grimoire-ts18-hub-{_id}";

    /// <summary>The egress proxy container's name.</summary>
    public string EgressContainer => $"grimoire-ts18-egress-{_id}";

    /// <summary>The scripted model's control URL, published to the test process on loopback.</summary>
    public string ModelControlUrl { get; private set; } = "";

    /// <summary>Stands the deployment up and waits until the hub serves.</summary>
    public static async Task<ContainerDeployment> Start(string script, CancellationToken cancellationToken)
    {
        var deployment = new ContainerDeployment();
        try
        {
            await deployment.Up(script, cancellationToken);
            return deployment;
        }
        catch
        {
            await deployment.DisposeAsync();
            throw;
        }
    }

    private async Task Up(string script, CancellationToken cancellationToken)
    {
        var internalNetwork = Track(_networks, $"grimoire-ts18-internal-{_id}");
        var outsideNetwork = Track(_networks, $"grimoire-ts18-outside-{_id}");
        await Must(["network", "create", "--internal", internalNetwork]);
        await Must(["network", "create", outsideNetwork]);

        var wiki = Track(_volumes, $"grimoire-ts18-wiki-{_id}");
        var state = Track(_volumes, $"grimoire-ts18-state-{_id}");
        await Must(["volume", "create", wiki]);
        await Must(["volume", "create", state]);

        // The model upstream: on the outside network only, unreachable from the hub by construction.
        var model = Track(_containers, $"grimoire-ts18-model-{_id}");
        await Must([
            "run", "--detach", "--name", model,
            "--network", outsideNetwork, "--network-alias", "model",
            "--publish", "127.0.0.1::8787",
            "--env", $"GRIMOIRE_SCRIPT={script}",
            BuiltImages.Model,
        ]);
        var published = (await Must(["port", model, "8787/tcp"])).Split('\n')[0].Trim();
        ModelControlUrl = $"http://127.0.0.1:{published[(published.LastIndexOf(':') + 1)..]}";
        await WaitForTheModel(cancellationToken);

        // The egress proxy: the only thing on both networks that forwards outward.
        Track(_containers, EgressContainer);
        await Must([
            "create", "--name", EgressContainer,
            "--network", internalNetwork, "--network-alias", "egress",
            "--read-only", "--tmpfs", "/tmp", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--env", "GRIMOIRE_EGRESS_MODEL_UPSTREAM=http://model:8787",
            "--env", $"GRIMOIRE_EGRESS_MODEL_API_KEY={UpstreamCredential}",
            "--env", $"GRIMOIRE_EGRESS_INTERNAL_TOKEN={InternalToken}",
            BuiltImages.Egress,
        ]);
        await Must(["network", "connect", outsideNetwork, EgressContainer]);
        await Must(["start", EgressContainer]);

        // The wiki needs a commit before the first ingest — compose's wiki-init, the same command.
        await Must([
            "run", "--rm", "--network", "none", "--volume", $"{wiki}:/data/wiki", "--entrypoint", "sh",
            BuiltImages.Hub, "-c",
            "git init --quiet --initial-branch=main /data/wiki && git -C /data/wiki -c user.name=Grimoire "
            + "-c user.email=grimoire@localhost commit --quiet --allow-empty -m 'Initialise the wiki'",
        ]);

        Track(_containers, HubContainer);
        await Must([
            "run", "--detach", "--name", HubContainer,
            "--network", internalNetwork,
            "--read-only", "--tmpfs", "/tmp", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--volume", $"{wiki}:/data/wiki", "--volume", $"{state}:/data/state",
            "--env", "GRIMOIRE_MODEL_BASE_URL=http://egress:8080",
            "--env", $"GRIMOIRE_MODEL_TOKEN={InternalToken}",
            "--env", "GRIMOIRE_FETCH_PROXY=http://egress:8080/fetch",
            BuiltImages.Hub,
        ]);

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            if ((await Hub("GET", "/healthz")).Status is 200)
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("The hub container did not serve /healthz within two minutes:\n"
            + (await Run(["logs", HubContainer])).Stdout);
    }

    // A started container is not a listening server: an ingest submitted before the model binds
    // would fail on a race rather than on anything under test.
    private async Task WaitForTheModel(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync($"{ModelControlUrl}/__requests", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Not listening yet.
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("The scripted model container did not answer within 60 seconds.");
    }

    /// <summary>An HTTP request to the hub, made from inside its container with its own Node.</summary>
    public async Task<(int Status, string Body)> Hub(string method, string path, string? json = null)
    {
        const string script =
            "const [m,p,b]=process.argv.slice(1);"
            + "fetch('http://127.0.0.1:8080'+p,{method:m,headers:{'content-type':'application/json'},body:b||undefined})"
            + ".then(async r=>process.stdout.write(JSON.stringify({status:r.status,body:await r.text()})))"
            + ".catch(e=>process.stdout.write(JSON.stringify({status:0,body:String(e)})))";
        var result = await Run(["exec", HubContainer, "node", "-e", script, method, path, json ?? ""]);
        if (!result.Succeeded || result.Stdout.Length is 0)
        {
            return (0, result.Stderr);
        }

        var answer = JsonDocument.Parse(result.Stdout).RootElement;
        return (answer.GetProperty("status").GetInt32(), answer.GetProperty("body").GetString() ?? "");
    }

    /// <summary>Submits pasted text and returns the task identifier.</summary>
    public async Task<string> SubmitText(string text)
    {
        var (status, body) = await Hub("POST", "/api/tasks", JsonSerializer.Serialize(new { kind = "text", value = text }));
        Assert.True(status is 201 or 202 or 200, $"Submission answered {status}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Opens a task.</summary>
    public async Task<JsonElement> OpenTask(string id)
    {
        var (status, body) = await Hub("GET", $"/api/tasks/{id}");
        Assert.True(status is 200, $"Opening task {id} answered {status}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Waits for a task to reach one of the given states.</summary>
    public async Task<JsonElement> WaitFor(string id, TimeSpan timeout, CancellationToken cancellationToken, params string[] states)
    {
        var deadline = DateTime.UtcNow + timeout;
        JsonElement task = default;
        while (DateTime.UtcNow < deadline)
        {
            task = await OpenTask(id);
            if (states.Contains(task.GetProperty("state").GetString()))
            {
                return task;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"Task {id} was '{task.GetProperty("state").GetString()}', never {string.Join('/', states)}.");
    }

    /// <summary>Runs a shell command inside the hub container.</summary>
    public Task<CommandResult> InHub(string shell) => Run(["exec", HubContainer, "sh", "-c", shell]);

    /// <summary>What the model upstream received, via its control endpoint.</summary>
    public async Task<IReadOnlyList<JsonElement>> ModelRequests(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync($"{ModelControlUrl}/__requests", cancellationToken);
        return [.. JsonDocument.Parse(json).RootElement.EnumerateArray().Select(element => element.Clone())];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var container in Enumerable.Reverse(_containers))
        {
            await Run(["rm", "--force", "--volumes", container]);
        }

        foreach (var volume in _volumes)
        {
            await Run(["volume", "rm", "--force", volume]);
        }

        foreach (var network in _networks)
        {
            await Run(["network", "rm", network]);
        }
    }

    private static string Track(List<string> list, string name)
    {
        list.Add(name);
        return name;
    }
}

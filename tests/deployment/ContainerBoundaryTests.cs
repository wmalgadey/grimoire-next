using System.Text.Json;

namespace Grimoire.Tests.Deployment;

/// <summary>
/// T109 / TS-18 — quality gate 4, the network half (ADR-0007, ADR-0010, FR-010). Against the
/// <b>built</b> <c>grimoire-hub</c> image on a network with no route off the host and exactly one
/// reachable destination, the egress proxy: the container runs unprivileged on a read-only root,
/// a full ingest completes with nothing leaving but model requests through the proxy, no process in
/// the container holds the upstream credential, and a broken egress path is a recorded reason and a
/// failed readiness check — not a hang.
/// </summary>
/// <remarks>
/// Needs a container runtime. Without one each test skips and says why; under CI a missing runtime
/// fails instead, because this is a gate and it runs on every PR.
/// </remarks>
public sealed class ContainerBoundaryTests(BuiltImages images) : IClassFixture<BuiltImages>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // The fixture's work is the build; the tests only need it to have happened.
    private readonly BuiltImages _images = images;

    [Fact]
    public async Task RunsAsANonRootUserOnAReadOnlyRootFilesystem()
    {
        ContainerRuntime.RequireOrSkip();
        await using var deployment = await ContainerDeployment.Start("no-op", Cancel);

        var user = await ContainerRuntime.Must(["image", "inspect", "--format", "{{.Config.User}}", BuiltImages.Hub]);
        Assert.False(string.IsNullOrWhiteSpace(user), "The image names no user, so it runs as root.");
        Assert.NotEqual("root", user);
        Assert.NotEqual("0", user);

        var uid = await deployment.InHub("id -u");
        Assert.True(uid.Succeeded, uid.Stderr);
        Assert.NotEqual("0", uid.Stdout.Trim());

        // The root filesystem refuses a write — including where the application itself lives.
        foreach (var path in new[] { "/app/probe", "/usr/local/bin/probe", "/etc/probe", "/home/probe" })
        {
            var write = await deployment.InHub($"touch {path}");
            Assert.False(write.Succeeded, $"{path} was writable on what should be a read-only root filesystem.");
        }

        // Writable exactly where the contract says: the two volumes and the tmpfs.
        foreach (var directory in new[] { "/data/wiki", "/data/state", "/tmp" })
        {
            var write = await deployment.InHub($"touch {directory}/.probe && rm {directory}/.probe");
            Assert.True(write.Succeeded, $"{directory} should be writable: {write.Stderr}");
        }
    }

    [Fact]
    public async Task CompletesAnIngestWithNothingLeavingButModelRequestsThroughTheProxy()
    {
        ContainerRuntime.RequireOrSkip();
        await using var deployment = await ContainerDeployment.Start("read-then-write", Cancel);

        var id = await deployment.SubmitText("Notes worth keeping, submitted inside the boundary.");
        var task = await deployment.WaitFor(id, TimeSpan.FromMinutes(3), Cancel, "completed", "failed");

        Assert.True(
            task.GetProperty("state").GetString() is "completed",
            $"The ingest did not complete: {task.GetProperty("failureReason")}");
        // Everything the proxy forwarded for this deployment was a model request, attributed to
        // the run. The SDK does address the gateway with the odd request of its own (a connectivity
        // probe, `/api/hello`, arrives even with CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1): the
        // proxy answers it and forwards nothing, because only /v1/* has an upstream.
        var access = await AccessLog(deployment);
        var model = access.Where(entry => entry.Path.StartsWith("/v1/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(model);
        Assert.All(model, entry => Assert.Equal(id, entry.Run));
        Assert.All(
            access.Except(model),
            entry => Assert.True(entry.Status is 404, $"{entry.Path} answered {entry.Status}: something other than a model request was forwarded."));
        Assert.Equal(model.Count, (await deployment.ModelRequests(Cancel)).Count);

        // And nothing else is reachable at all: not the model upstream directly, not the internet.
        foreach (var destination in new[] { "http://model:8787/", "http://1.1.1.1/", "https://api.anthropic.com/" })
        {
            var (status, body) = await deployment.Hub("GET", "/healthz"); // the hub itself still answers
            Assert.Equal(200, status);
            var probe = await deployment.InHub(
                "node -e \"fetch(process.argv[1],{signal:AbortSignal.timeout(5000)})"
                + ".then(()=>process.exit(0),()=>process.exit(1))\" " + destination);
            Assert.False(probe.Succeeded, $"The hub container reached {destination} directly. {body}");
        }
    }

    [Fact]
    public async Task HoldsNoUpstreamCredentialAnywhereAndGivesTheRunnerOnlyTheOpaqueToken()
    {
        ContainerRuntime.RequireOrSkip();
        await using var deployment = await ContainerDeployment.Start("write-then-hang", Cancel);

        var id = await deployment.SubmitText("Notes for a run that stays in flight.");
        await deployment.WaitFor(id, TimeSpan.FromMinutes(2), Cancel, "running");

        // The runner, while it runs: its environment is the replaced one the hub built.
        var runner = await WaitForRunnerEnvironment(deployment);
        Assert.Equal(ContainerDeployment.InternalToken, runner.GetValueOrDefault("ANTHROPIC_AUTH_TOKEN"));
        Assert.Equal("http://egress:8080", runner.GetValueOrDefault("ANTHROPIC_BASE_URL"));
        Assert.Equal("1", runner.GetValueOrDefault("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"));
        Assert.False(runner.ContainsKey("ANTHROPIC_API_KEY"), "The runner was given ANTHROPIC_API_KEY.");

        // No process in the container — hub, runner, or anything the runner started — carries the
        // upstream credential, under any name.
        var everyEnvironment = await deployment.InHub(
            "for p in /proc/[0-9]*; do tr '\\0' '\\n' < $p/environ 2>/dev/null; done");
        Assert.True(everyEnvironment.Succeeded, everyEnvironment.Stderr);
        Assert.Contains("ANTHROPIC_AUTH_TOKEN=", everyEnvironment.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(ContainerDeployment.UpstreamCredential, everyEnvironment.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("ANTHROPIC_API_KEY=", everyEnvironment.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlockedEgressPathIsARecordedReasonAndAFailedReadinessCheckNotAHang()
    {
        ContainerRuntime.RequireOrSkip();
        await using var deployment = await ContainerDeployment.Start("read-then-write", Cancel);

        await ContainerRuntime.Must(["stop", "--time", "5", deployment.EgressContainer]);

        var id = await deployment.SubmitText("Notes submitted while the egress path is down.");
        var task = await deployment.WaitFor(id, TimeSpan.FromMinutes(2), Cancel, "completed", "failed");

        Assert.Equal("failed", task.GetProperty("state").GetString());
        var reason = task.GetProperty("failureReason").GetString() ?? "";
        Assert.Contains("egress:8080", reason, StringComparison.Ordinal);

        var wiki = await deployment.InHub("git -C /data/wiki rev-list --count HEAD && git -C /data/wiki status --porcelain");
        Assert.Equal("1", wiki.Stdout.Trim());

        var (status, body) = await deployment.Hub("GET", "/readyz");
        Assert.Equal(503, status);
        var readiness = JsonDocument.Parse(body).RootElement;
        Assert.Equal("failed", readiness.GetProperty("checks").GetProperty("egress").GetString());
        Assert.Equal("ok", readiness.GetProperty("checks").GetProperty("wikiRepo").GetString());
        Assert.Equal("ok", readiness.GetProperty("checks").GetProperty("stateDb").GetString());
    }

    private sealed record AccessEntry(string Path, string Run, int Status);

    private static async Task<IReadOnlyList<AccessEntry>> AccessLog(ContainerDeployment deployment)
    {
        var logs = await ContainerRuntime.Run(["logs", deployment.EgressContainer]);
        var entries = new List<AccessEntry>();
        foreach (var line in (logs.Stdout + "\n" + logs.Stderr).Split('\n'))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            var element = JsonDocument.Parse(line).RootElement;
            if (element.GetProperty("Category").GetString() is not "Grimoire.Egress.Access")
            {
                continue;
            }

            var state = element.GetProperty("State");
            entries.Add(new AccessEntry(
                state.GetProperty("Path").GetString() ?? "",
                state.GetProperty("Run").GetString() ?? "",
                int.Parse(state.GetProperty("Status").ToString(), System.Globalization.CultureInfo.InvariantCulture)));
        }

        return entries;
    }

    private static async Task<Dictionary<string, string>> WaitForRunnerEnvironment(ContainerDeployment deployment)
    {
        // Anchored on the executable, so the shell running this search — whose own command line
        // contains the pattern — is not what it finds.
        const string find =
            "for p in /proc/[0-9]*; do "
            + "if tr '\\0' ' ' < $p/cmdline 2>/dev/null | grep -q '^[^ ]*node [^ ]*agentrun/dist/main[.]js'; then "
            + "tr '\\0' '\\n' < $p/environ; exit 0; fi; done; exit 1";

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var result = await deployment.InHub(find);
            if (result.Succeeded)
            {
                return result.Stdout
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split('=', 2))
                    .Where(pair => pair.Length is 2)
                    .GroupBy(pair => pair[0])
                    .ToDictionary(group => group.Key, group => group.First()[1]);
            }

            await Task.Delay(500, Cancel);
        }

        throw new TimeoutException("No runner process appeared in the hub container.");
    }
}

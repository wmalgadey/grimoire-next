using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Grimoire.Tests.Support;

/// <summary>
/// The hub run as a <b>real operating-system process</b>, so its stdout is the stdout production
/// has and a real <c>SIGTERM</c> can be sent to it.
/// </summary>
/// <remarks>
/// In-process hosting (<see cref="GrimoireHub"/>) is right for asserting behaviour through the
/// composition root; it cannot assert the logging transport or the shutdown signal, because
/// neither exists in a test host. Those two need this.
/// </remarks>
public sealed class HubProcess : IDisposable
{
    // The suite and the hub come out of the same `dotnet build`, so the hub binary to run is the
    // one built in this suite's configuration. Without saying so, `dotnet run --no-build` looks
    // for a Debug build, and a Release-only CI has none: the hub exits 1 before it serves.
    private const string Configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private readonly Process _process;
    private readonly List<string> _stdout = [];
    private readonly List<string> _stderr = [];
    private readonly object _lock = new();

    private HubProcess(Process process, string baseAddress)
    {
        _process = process;
        BaseAddress = baseAddress;
        Client = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    /// <summary>Where the hub is listening.</summary>
    public string BaseAddress { get; }

    /// <summary>A client against the running hub.</summary>
    public HttpClient Client { get; }

    /// <summary>Every line the hub has written to stdout so far.</summary>
    public IReadOnlyList<string> Stdout
    {
        get
        {
            lock (_lock)
            {
                return [.. _stdout];
            }
        }
    }

    /// <summary>Whether the process has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>The process's exit code. Only meaningful once it has exited.</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>Starts a hub process against the given wiki, state file and model endpoint.</summary>
    public static async Task<HubProcess> Start(
        WikiRepositoryFixture wiki,
        ScriptedModelFixture? model = null,
        string? stateDatabasePath = null,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? arguments = null)
    {
        var port = FreePort();
        var root = ScriptedModelFixture.RepositoryRoot;
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--project");
        info.ArgumentList.Add(Path.Combine(root, "src", "hub"));
        info.ArgumentList.Add("--no-build");
        info.ArgumentList.Add("--configuration");
        info.ArgumentList.Add(Configuration);
        if (arguments is { Count: > 0 })
        {
            // Past `--`, so they reach the hub itself rather than `dotnet run`.
            info.ArgumentList.Add("--");
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
        }

        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        info.Environment["GRIMOIRE_WIKI_REPO"] = wiki.Path;
        info.Environment["GRIMOIRE_STATE_DB"] = stateDatabasePath
            ?? Path.Combine(Path.GetTempPath(), $"grimoire-state-{Guid.NewGuid():N}.db");
        info.Environment["GRIMOIRE_MODEL_BASE_URL"] = model?.BaseUrl ?? "http://127.0.0.1:1";
        info.Environment["GRIMOIRE_MODEL_TOKEN"] = "an-opaque-internal-token";
        info.Environment["GRIMOIRE_INSTRUCTION"] = Path.Combine(root, "src", "instructions", "ingest.md");

        foreach (var (name, value) in extraEnvironment ?? new Dictionary<string, string?>())
        {
            info.Environment[name] = value;
        }

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("The hub process could not be started.");

        var hub = new HubProcess(process, $"http://127.0.0.1:{port}");
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (hub._lock)
                {
                    hub._stdout.Add(args.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (hub._lock)
                {
                    hub._stderr.Add(args.Data);
                }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await hub.WaitUntilServing(cancellationToken);
        return hub;
    }

    /// <summary>
    /// Starts a hub, drives one ingest to its end, and returns everything the process logged —
    /// the shape the observability transport assertions need.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RunOneIngest(
        string scriptName,
        CancellationToken cancellationToken)
    {
        using var wiki = new WikiRepositoryFixture();
        using var model = ScriptedModelFixture.Start(scriptName);
        using var hub = await Start(wiki, model, cancellationToken: cancellationToken);

        var response = await hub.Client.PostAsJsonAsync(
            "/api/tasks", new { kind = "text", value = "notes worth keeping" }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var id = created.GetProperty("id").GetString()!;

        await hub.WaitForEnd(id, cancellationToken);
        // Give the logger a moment to flush the last line before the process is torn down.
        await Task.Delay(500, cancellationToken);
        return hub.Stdout;
    }

    /// <summary>Submits pasted text and returns the created task's identifier.</summary>
    public async Task<string> SubmitText(string value, CancellationToken cancellationToken)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks", new { kind = "text", value }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return created.GetProperty("id").GetString()!;
    }

    /// <summary>Opens a task in whatever state it is in (FR-020).</summary>
    public async Task<JsonElement> GetTask(string taskId, CancellationToken cancellationToken) =>
        await Client.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}", cancellationToken);

    /// <summary>
    /// Waits until a task reaches one particular state. Recovery and shutdown are about what
    /// happens to a run <i>in flight</i>, so a suite has to be able to catch one there.
    /// </summary>
    public async Task<JsonElement> WaitForState(
        string taskId, string state, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        JsonElement task = default;

        while (DateTime.UtcNow < deadline)
        {
            task = await GetTask(taskId, cancellationToken);
            if (task.GetProperty("state").GetString() == state)
            {
                return task;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(
            $"Task {taskId} was '{task.GetProperty("state").GetString()}', never '{state}'.");
    }

    /// <summary>
    /// Kills the hub without a signal it can handle — an OOM kill, a node going away. The
    /// ungraceful path startup recovery is the backstop for (contracts/deployment.md Lifecycle).
    /// </summary>
    public void KillUngracefully()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10_000);
        }
    }

    /// <summary>Waits until a task reaches a terminal state.</summary>
    public async Task<JsonElement> WaitForEnd(string taskId, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        JsonElement task = default;

        while (DateTime.UtcNow < deadline)
        {
            task = await Client.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}", cancellationToken);
            if (task.GetProperty("state").GetString() is "completed" or "failed" or "reverted")
            {
                return task;
            }

            await Task.Delay(150, cancellationToken);
        }

        throw new TimeoutException($"Task {taskId} did not reach a terminal state in time.");
    }

    /// <summary>
    /// The agent runners this hub has running right now — found among its own descendants, so a
    /// runner belonging to any other hub on the machine is never counted.
    /// </summary>
    public IReadOnlyList<int> RunnerProcessIds()
    {
        using var ps = Process.Start(new ProcessStartInfo("ps")
        {
            ArgumentList = { "-A", "-o", "pid=,ppid=,command=" },
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var listing = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();

        var processes = listing
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length is 3)
            .Select(fields => (Pid: int.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture),
                Parent: int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture),
                Command: fields[2]))
            .ToList();

        var descendants = new HashSet<int> { _process.Id };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var process in processes)
            {
                if (descendants.Contains(process.Parent) && descendants.Add(process.Pid))
                {
                    grew = true;
                }
            }
        }

        return [.. processes
            .Where(process => descendants.Contains(process.Pid)
                && process.Command.Contains("agentrun/dist/main.js", StringComparison.Ordinal))
            .Select(process => process.Pid)];
    }

    /// <summary>Sends a real <c>SIGTERM</c> and waits for the process to exit.</summary>
    public async Task<bool> Terminate(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_process.HasExited)
        {
            return true;
        }

        // A real signal, not Kill(): the graceful path is what is under test (TS-19).
        using var kill = Process.Start(new ProcessStartInfo("kill")
        {
            ArgumentList = { "-TERM", _process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;
        await kill.WaitForExitAsync(cancellationToken);

        try
        {
            return await Task.Run(() => _process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Client.Dispose();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
    }

    private async Task WaitUntilServing(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                // Both streams: a hub that fails inside the app logs JSON to stdout, while a
                // hub that `dotnet run` could not even start explains itself on stderr.
                string[] stdout, stderr;
                lock (_lock)
                {
                    stdout = [.. _stdout];
                    stderr = [.. _stderr];
                }

                throw new InvalidOperationException(
                    $"The hub exited during startup with code {_process.ExitCode}.{Environment.NewLine}"
                    + $"stdout:{Environment.NewLine}{string.Join(Environment.NewLine, stdout)}{Environment.NewLine}"
                    + $"stderr:{Environment.NewLine}{string.Join(Environment.NewLine, stderr)}");
            }

            try
            {
                using var response = await Client.GetAsync("/healthz", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException("The hub did not start serving in time.");
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}

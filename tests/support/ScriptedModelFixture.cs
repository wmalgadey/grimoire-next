using System.Diagnostics;
using System.Text.Json;

namespace Grimoire.Tests.Support;

/// <summary>One request the double received.</summary>
/// <param name="At">Arrival timestamp.</param>
/// <param name="Method">HTTP method.</param>
/// <param name="Url">Request path.</param>
/// <param name="ByteLength">Size of the body as received.</param>
/// <param name="Body">The body as received, so a test can look for the source in it.</param>
public sealed record RecordedModelRequest(
    DateTimeOffset At,
    string Method,
    string Url,
    int ByteLength,
    string Body);

/// <summary>
/// The single sanctioned test double (ADR-0004), run as a real process on a real socket and
/// reached through <c>ANTHROPIC_BASE_URL</c> — the same variable the egress proxy occupies in
/// production, so the SDK's own agent loop executes under test.
/// </summary>
/// <remarks>
/// The C# suites drive it over HTTP rather than in-process: it is a Node package
/// (<c>tests/scripted-model/</c>) shared with the Vitest runner suite, and one implementation of
/// the double is the point (constitution VII.2).
/// </remarks>
public sealed class ScriptedModelFixture : IDisposable
{
    private readonly Process _process;

    private ScriptedModelFixture(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
    }

    /// <summary>What the hub is configured with as <c>GRIMOIRE_MODEL_BASE_URL</c>.</summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Starts the double replaying one named script (see <c>tests/scripted-model/src/scripts.ts</c>).
    /// </summary>
    public static ScriptedModelFixture Start(string scriptName)
    {
        var entry = Path.Combine(RepositoryRoot, "tests", "scripted-model", "dist", "server.js");
        if (!File.Exists(entry))
        {
            throw new InvalidOperationException(
                $"The scripted model is not built. Run `npm --prefix tests/scripted-model run build`. Looked for {entry}.");
        }

        var info = new ProcessStartInfo("node")
        {
            WorkingDirectory = Path.Combine(RepositoryRoot, "tests", "scripted-model"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(entry);
        info.Environment["GRIMOIRE_SCRIPT"] = scriptName;

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("The scripted model could not be started.");

        // The server announces its ephemeral port on stdout as soon as it is listening.
        var announcement = process.StandardOutput.ReadLine()
            ?? throw new InvalidOperationException(
                $"The scripted model exited before announcing a port: {process.StandardError.ReadToEnd()}");

        var baseUrl = JsonDocument.Parse(announcement).RootElement.GetProperty("baseUrl").GetString()!;
        return new ScriptedModelFixture(process, baseUrl);
    }

    /// <summary>
    /// Switches the script mid-suite, so one hub — which has exactly one model endpoint — can
    /// drive more than one run with different scripted behaviour.
    /// </summary>
    public async Task UseScript(string scriptName, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var response = await client.PostAsync(
            $"{BaseUrl}/__script/{Uri.EscapeDataString(scriptName)}", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Sets a variable the double substitutes for <c>{{name}}</c> in the tool inputs it scripts, so a
    /// script can aim at something only the suite knows — a canary path created for this test.
    /// </summary>
    public async Task SetVariable(string name, string value, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var response = await client.PostAsync(
            $"{BaseUrl}/__var/{Uri.EscapeDataString(name)}", new StringContent(value), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// What the double received, in arrival order — the record TS-04 asserts the source's bytes
    /// against and TS-07 compares timestamps against.
    /// </summary>
    public async Task<IReadOnlyList<RecordedModelRequest>> Requests(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var json = await client.GetStringAsync($"{BaseUrl}/__requests", cancellationToken);
        return JsonSerializer.Deserialize<List<RecordedModelRequest>>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }

    /// <summary>The raw request bodies the double received, in arrival order.</summary>
    public async Task<IReadOnlyList<string>> RequestBodies(CancellationToken cancellationToken) =>
        (await Requests(cancellationToken)).Select(request => request.Body).ToList();

    /// <summary>
    /// When the double was first asked anything. TS-07 asserts the instruction version and the
    /// tool grant were persisted <b>before</b> this moment (FR-013, FR-014).
    /// </summary>
    public async Task<DateTimeOffset?> FirstRequestAt(CancellationToken cancellationToken) =>
        (await Requests(cancellationToken)).Select(request => request.At).Cast<DateTimeOffset?>().FirstOrDefault();

    /// <inheritdoc />
    public void Dispose()
    {
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

    /// <summary>Walks up from the test binary to the repository root.</summary>
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Grimoire.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not find the repository root from the test binary.");
        }
    }
}

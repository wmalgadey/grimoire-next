using System.Diagnostics;
using System.Text.Json.Nodes;
using Grimoire.Agent;
using Grimoire.Agent.Adapters;
using Grimoire.Hub;
using Grimoire.Wiki.Adapters;
using Microsoft.AspNetCore.Builder;

namespace Grimoire.Contract.Tests;

/// <summary>
/// A real wiki in a temporary directory and a hub serving that run's tools, for the real
/// <c>claude</c> CLI to work against.
/// </summary>
internal sealed class RealRun : IAsyncDisposable
{
    /// <summary>
    /// A pinned model id, never an alias: what <c>modelUsage</c> comes back keyed by is how the
    /// run proves it was served by the model it asked for (research.md R-11).
    /// </summary>
    public const string PinnedModel = "claude-haiku-4-5-20251001";

    private readonly WebApplication app;

    private RealRun(WebApplication app, string root, string wikiRoot, Uri address)
    {
        this.app = app;
        Root = root;
        WikiRoot = wikiRoot;
        Address = address;
        Harness = new HarnessProcess(new HarnessSettings("claude", Path.Combine(root, "cwd"), address));
    }

    public string Root { get; }

    public string WikiRoot { get; }

    public Uri Address { get; }

    public HarnessProcess Harness { get; }

    public ToolGrant Grant { get; } = ToolGrant.Ingest(TimeProvider.System);

    public static async Task<RealRun> StartAsync(CancellationToken cancellationToken)
    {
        var root = Directory.CreateTempSubdirectory("grimoire-contract-").FullName;
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);
        Directory.CreateDirectory(Path.Combine(root, "cwd"));

        var instruction = Path.Combine(root, "ingest.md");
        var purpose = Path.Combine(root, "purpose.md");
        await File.WriteAllTextAsync(instruction, "Work the text into the wiki.", cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(purpose, "A wiki about anything.", cancellationToken).ConfigureAwait(false);

        var app = HubApplication.Build(
            ["--urls", "http://127.0.0.1:0"],
            new HubOptions(instruction, purpose, wikiRoot, PinnedModel),
            new HarnessProcess(HarnessSettings.Default(new Uri("http://127.0.0.1:1"))),
            new FileSystemWikiStore(wikiRoot),
            TimeProvider.System);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        return new RealRun(app, root, wikiRoot, new Uri(app.Urls.First()));
    }

    public AgentDispatch Dispatch(string prompt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), prompt, Grant, PinnedModel);

    /// <summary>
    /// Runs the real CLI with exactly the argv <c>HarnessProcess</c> builds and returns every
    /// NDJSON message it wrote. This is how the CLI's own answers — the tool surface it reports
    /// and the model it was served by — are read without widening the port for a test.
    /// </summary>
    public async Task<IReadOnlyList<JsonObject>> TranscriptAsync(AgentDispatch dispatch, TimeSpan patience)
    {
        var start = new ProcessStartInfo("claude")
        {
            WorkingDirectory = Path.Combine(Root, "cwd"),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in HarnessProcess.ArgumentsFor(dispatch, Address))
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove("ANTHROPIC_API_KEY");

        using var process = Process.Start(start)!;
        using var patienceSource = new CancellationTokenSource(patience);

        var message = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = dispatch.Prompt },
        };
        await process.StandardInput.WriteLineAsync(message.ToJsonString()).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(patienceSource.Token).ConfigureAwait(false);

        var messages = new List<JsonObject>();

        try
        {
            while (await process.StandardOutput.ReadLineAsync(patienceSource.Token).ConfigureAwait(false) is { } line)
            {
                if (JsonNode.Parse(line) is JsonObject parsed)
                {
                    messages.Add(parsed);
                    if (parsed["type"]?.GetValue<string>() == "result")
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Whatever arrived before patience ran out is what the test reads.
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        return messages;
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(Root, recursive: true);
    }
}

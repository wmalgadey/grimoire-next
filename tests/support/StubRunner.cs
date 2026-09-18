namespace Grimoire.Tests.Support;

/// <summary>
/// A stand-in for the agent runner that speaks the real hub↔runner protocol and then misbehaves in
/// one scripted way — crashes, reports a wider grant, reports success and then exits non-zero.
/// </summary>
/// <remarks>
/// Not a double of the LLM, and not a mock of anything the hub owns: it exercises the hub's side of
/// a process boundary against a peer the real runner never becomes on its own, so the hub's
/// handling of that peer is tested rather than assumed. The hub is pointed at it through
/// <c>GRIMOIRE_ROOT</c>, the variable it resolves <c>src/agentrun/dist/main.js</c> from.
/// </remarks>
public sealed class StubRunner : IDisposable
{
    /// <summary>The protocol helpers every stub starts with.</summary>
    private const string Prelude = """
        const readline = require("node:readline");
        const fs = require("node:fs");
        const emit = (event) => process.stdout.write(JSON.stringify(event) + "\n");
        const exitAfterFlush = (code) => process.stdout.write("", () => process.exit(code));
        const received = [];
        const waiters = [];
        readline.createInterface({ input: process.stdin }).on("line", (line) => {
          if (!line.trim()) return;
          const message = JSON.parse(line);
          received.push(message);
          for (const waiter of waiters.splice(0)) waiter(message);
        });
        const next = (type) => new Promise((resolve) => {
          const found = received.find((message) => message.type === type);
          if (found) return resolve(found);
          const wait = (message) => (message.type === type ? resolve(message) : waiters.push(wait));
          waiters.push(wait);
        });
        const granted = ["mcp__wiki__read_page", "mcp__wiki__write_page"];
        const announce = (tools = granted) => {
          emit({ type: "instruction_loaded", path: "stub.md", sha256: "0".repeat(64), byteLength: 0 });
          emit({ type: "tool_grant", tools });
        };
        """;

    private StubRunner(string root) => Root = root;

    /// <summary>The directory to hand the hub as <c>GRIMOIRE_ROOT</c>.</summary>
    public string Root { get; }

    /// <summary>The environment that points a hub at this stub.</summary>
    public IReadOnlyDictionary<string, string?> Environment =>
        new Dictionary<string, string?> { ["GRIMOIRE_ROOT"] = Root };

    /// <summary>Announces the configured grant, is told to proceed, and dies with no <c>run_end</c>.</summary>
    public static StubRunner CrashesAfterProceed(int exitCode) => Create($$"""
        (async () => {
          announce();
          await next("proceed");
          process.exit({{exitCode}});
        })();
        """);

    /// <summary>
    /// Writes into the wiki, reports a completed run, and then exits non-zero — a crash on the way
    /// out, after the outcome was already written.
    /// </summary>
    public static StubRunner ReportsCompletedThenExits(int exitCode) => Create($$"""
        (async () => {
          announce();
          await next("proceed");
          fs.writeFileSync("stub-written.md", "# Written by a runner that then crashed\n");
          emit({ type: "run_end", outcome: "completed", failureReason: null,
                 commitMessage: "stub", toolCallCount: 0, modelEndpointStatus: null });
          exitAfterFlush({{exitCode}});
        })();
        """);

    /// <summary>
    /// Reports a grant wider than the one the hub configured, and calls the model the moment it is
    /// told to proceed — so a hub that let it proceed would show up as a model request.
    /// </summary>
    public static StubRunner ReportsAWiderGrant() => Create("""
        (async () => {
          announce([...granted, "Bash"]);
          await next("proceed");
          await fetch(process.env.ANTHROPIC_BASE_URL + "/v1/messages", { method: "POST", body: "{}" });
          emit({ type: "run_end", outcome: "completed", failureReason: null,
                 commitMessage: "stub", toolCallCount: 0, modelEndpointStatus: null });
          exitAfterFlush(0);
        })();
        """);

    /// <summary>
    /// Writes into the wiki, sends a line that is not a protocol event, and carries on writing —
    /// a runner the hub has stopped listening to but that has not stopped.
    /// </summary>
    public static StubRunner EmitsAMalformedLineThenKeepsWriting() => Create("""
        (async () => {
          announce();
          await next("proceed");
          fs.writeFileSync("before-the-bad-line.md", "# Written before the bad line\n");
          process.stdout.write("this is not a protocol event\n");
          setTimeout(() => fs.writeFileSync("after-the-bad-line.md", "# Written after\n"), 4000);
          setTimeout(() => process.exit(0), 30000);
        })();
        """);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static StubRunner Create(string body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-stub-runner-{Guid.NewGuid():N}");
        var dist = Path.Combine(root, "src", "agentrun", "dist");
        Directory.CreateDirectory(dist);
        File.WriteAllText(Path.Combine(dist, "main.js"), Prelude + body);
        return new StubRunner(root);
    }
}

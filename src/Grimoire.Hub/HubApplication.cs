using Grimoire.Agent;
using Grimoire.Hub.Api;
using Grimoire.Hub.Mcp;
using Grimoire.Runs;
using Grimoire.Wiki;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub;

/// <summary>
/// What the hub was started with. Every run is served the same three: the wiki it writes into, the
/// two texts it is given, and the model it runs on.
/// </summary>
/// <param name="Model">
/// A pinned model id, never an alias and never the default — a start-up input, not a per-submission
/// choice (research.md R-11).
/// </param>
public sealed record HubOptions(
    string InstructionPath,
    string PurposeDescriptionPath,
    string WikiRoot,
    string Model);

/// <summary>
/// The composition root: the one place that knows every context (plan.md, Structure Decision).
/// </summary>
/// <remarks>
/// The agent adapter is a parameter rather than a lookup, because which one is in place is exactly
/// what distinguishes a real run from an exercised one: <c>HarnessProcess</c> runs the real CLI,
/// and the E2E suite supplies an in-memory adapter at the same port (Constitution III.9).
/// </remarks>
public static class HubApplication
{
    /// <summary>
    /// Everything the last Grimoire left behind, put back before this one serves anything
    /// (RUNS-004, RUNS-006).
    /// </summary>
    /// <remarks>
    /// The order is the requirement's rather than an implementation detail, because all of it is
    /// observable: an agent that outlived a stop Grimoire could not act on is terminated
    /// <b>first</b>, the runs that were in progress read failed <b>second</b>, and only then may
    /// anything start. The browser must never show failed while the agent is still at work, and no
    /// second run may begin beside a first that is still writing (research.md R-11).
    /// <para>
    /// A run with no recorded process never had a child, and one whose recorded identity is no
    /// longer a live process is left alone by the adapter. Nothing is resumed and nothing is
    /// retried; what an interrupted run wrote stays in the wiki (WIKI-003).
    /// </para>
    /// </remarks>
    public static void RestoreAfterAStop(ISubmissionStore submissions, SubmissionBoard board, IAgentHarness harness)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(harness);

        var held = submissions.Load();

        foreach (var identity in held
            .Where(s => s.WasUnderWay)
            .Select(s => s.Run!.AgentProcess)
            .OfType<AgentProcessIdentity>())
        {
            harness.Terminate(identity);
        }

        board.Restore(held);
    }

    /// <summary>
    /// The hub is going down: nothing further may start, and what is under way is stopped with it
    /// (RUNS-006).
    /// </summary>
    /// <remarks>
    /// The order is the point, and it is three steps rather than two. Stopping a run ends it, and
    /// an ending lets the next one start — so stopping first would dispatch an agent behind the
    /// shutdown. Closing admission alone is not enough either: a pump already past its own check
    /// can be holding a submission the board has handed out, and that one would be dispatched into
    /// a hub that had finished stopping, with nothing watching it. So: close, drain, then stop
    /// (RUNS-006).
    /// </remarks>
    public static async Task StopEverythingAsync(RunQueue queue, RunConductor conductor)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(conductor);

        queue.StopStartingRuns();
        await queue.DrainAsync().ConfigureAwait(false);
        await conductor.StopEverythingAsync().ConfigureAwait(false);
    }

    public static WebApplication Build(
        string[] args,
        HubOptions options,
        IAgentHarness harness,
        IWikiStore wiki,
        ISubmissionStore submissions,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(submissions);

        // The content root is the hub's own base directory rather than whatever directory it was
        // launched from, so the page under wwwroot/ is found the same way whether the hub was
        // started from the command line or built by a test.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // One line per entry, stamped, so that what the console says can be held against the clock
        // while a run is under way. UTC, because everything else the running system shows is UTC
        // too — the browser's list of submissions, and the `generated.at` on every page a run
        // writes — and a log that needs an offset applied before it can be compared is a log that
        // will be compared wrongly.
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            console.UseUtcTimestamp = true;
        });

        // What is left after this is one line as a request arrives and one as it finishes — the
        // agent's tool calls among them, which is how a run is watched. The three categories
        // turned down here only restate that same request: the endpoint that was selected, the
        // static file that was sent, the result type that was written.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.StaticFiles", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Result", LogLevel.Warning);

        // The wiki tools, served from the hub itself over streamable HTTP. Putting them here keeps
        // the stamping, the grant and both ceilings in one place where Fast tests reach them, and
        // leaves the agent one door into the wiki (research.md R-02).
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton(wiki);
        builder.Services.AddSingleton(sp => new RunAddress(
            sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), options.Model));
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<WikiToolsServer>();

        var app = builder.Build();

        // The page is static content served from wwwroot/ — one HTML file and one script, no
        // build step (research.md R-10).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var instructions = new InstructionLoader(options.InstructionPath, options.PurposeDescriptionPath);
        var board = new SubmissionBoard(clock, submissions);

        // The knot the conductor and the queue make, tied here because neither may hold the other
        // whole: a run that ends is what lets the next one start, and starting one is what gives
        // the conductor a run to watch. The composition root is where that is allowed to be known
        // (plan.md, Structure Decision).
        RunQueue? queue = null;
        var conductor = new RunConductor(board, harness, wiki, clock, () => queue!.PumpAsync());
        queue = new RunQueue(board, conductor, harness, instructions.Assemble, options.Model);

        var intake = new SubmissionIntake(board, queue);

        app.MapSubmissions(intake, board, queue, instructions.Read);

        // One endpoint per run: the identifier in the path is how a tool call is attributed to
        // its run. Unauthenticated and on loopback, per docs/product.md §2.
        app.MapMcp("/mcp/runs/{runId}");

        RestoreAfterAStop(submissions, board, harness);

        // The pump waits for the server. A run is told where its own tools are served before it
        // starts, so starting one before this hub is listening would hand the agent an address
        // that answers nothing and end the run on its first tool call (GUARD-001).
        app.Lifetime.ApplicationStarted.Register(() => _ = queue.PumpAsync());

        // No agent goes on working on a run Grimoire has ended (RUNS-006). The hook itself is
        // framework wiring and is not tested; what it calls is (Constitution III.8, research.md R-05).
        app.Lifetime.ApplicationStopping.Register(() => StopEverythingAsync(queue, conductor).GetAwaiter().GetResult());

        return app;
    }
}

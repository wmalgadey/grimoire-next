using Grimoire.Agent;
using Grimoire.Hub.Api;
using Grimoire.Hub.Mcp;
using Grimoire.Runs;
using Grimoire.Wiki;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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
    public static WebApplication Build(
        string[] args,
        HubOptions options,
        IAgentHarness harness,
        IWikiStore wiki,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The content root is the hub's own base directory rather than whatever directory it was
        // launched from, so the page under wwwroot/ is found the same way whether the hub was
        // started from the command line or built by a test.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

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
        var board = new SubmissionBoard(clock);
        var conductor = new RunConductor(board, harness, wiki, clock);
        var intake = new SubmissionIntake(board, harness, conductor, instructions.Assemble, options.Model);

        app.MapSubmissions(intake, instructions.Read);

        // One endpoint per run: the identifier in the path is how a tool call is attributed to
        // its run. Unauthenticated and on loopback, per docs/product.md §2.
        app.MapMcp("/mcp/runs/{runId}");

        return app;
    }
}

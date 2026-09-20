using Grimoire.Agent;
using Grimoire.Hub.Api;
using Grimoire.Runs;
using Microsoft.AspNetCore.Builder;

namespace Grimoire.Hub;

/// <summary>
/// The paths the hub was started with. Both texts every run receives are read from here, and the
/// absence of either refuses every submission (INGEST-003, Constitution V.1).
/// </summary>
public sealed record HubOptions(string InstructionPath, string PurposeDescriptionPath)
{
    /// <summary>
    /// Whether each of the two is in place right now. Reading their contents — and assembling the
    /// dispatch payload from them — is T033's <c>InstructionLoader</c>; this is only the absence
    /// check, and it is made per submission because INGEST-003 is about the state of those paths
    /// when a text is submitted, not when the hub started.
    /// </summary>
    public StartUpInputs Read() => new(
        InstructionPresent: File.Exists(InstructionPath),
        PurposeDescriptionPresent: File.Exists(PurposeDescriptionPath));
}

/// <summary>
/// The composition root: the one place that knows every context (plan.md, Structure Decision).
/// </summary>
/// <remarks>
/// The agent adapter is a parameter rather than a lookup, because which one is in place is exactly
/// what distinguishes a real run from an exercised one: <c>HarnessProcess</c> arrives with T031,
/// and the E2E suite supplies the in-memory adapter at this same port (Constitution III.9).
/// </remarks>
public static class HubApplication
{
    public static WebApplication Build(string[] args, HubOptions options, IAgentHarness harness, TimeProvider clock)
    {
        // The content root is the hub's own base directory rather than whatever directory it was
        // launched from, so the page under wwwroot/ is found the same way whether the hub was
        // started from the command line or built by a test.
        var app = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        }).Build();

        // The page is static content served from wwwroot/ — one HTML file and one script, no
        // build step (research.md R-10).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var intake = new SubmissionIntake(new SubmissionBoard(clock), harness);
        app.MapSubmissions(intake, options.Read);

        return app;
    }
}

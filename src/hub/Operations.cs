using Grimoire.Dispatch.Adapters;
using Grimoire.Tasks.Adapters;
using Grimoire.Wiki.Adapters;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Grimoire.Hub;

/// <summary>
/// Whether this replica should be taking traffic, and whether it is draining. Set to
/// <c>draining</c> by the <c>SIGTERM</c> path so traffic leaves before the process does
/// (contracts/deployment.md "SIGTERM", step 1).
/// </summary>
public sealed class DrainState
{
    private volatile bool _draining;

    /// <summary>Whether the hub has stopped accepting dispatches and is shutting down.</summary>
    public bool IsDraining => _draining;

    /// <summary>Marks the replica as draining. Irreversible for the life of the process.</summary>
    public void BeginDraining() => _draining = true;
}

/// <summary>
/// <c>/healthz</c> and <c>/readyz</c> (contracts/deployment.md). Both live in
/// <c>hub-api.openapi.yaml</c> under the <c>Operations</c> tag, so the contract drift test covers
/// them. Readiness uses the framework's own health-check registration rather than a hand-rolled
/// endpoint (constitution VII.1).
/// </summary>
public static class Operations
{
    /// <summary>The names the readiness body reports, one per dependency.</summary>
    public const string WikiRepositoryCheck = "wikiRepo";

    /// <summary>The operational store.</summary>
    public const string StateDatabaseCheck = "stateDb";

    /// <summary>The single permitted destination out of the hub container (ADR-0010).</summary>
    public const string EgressCheck = "egress";

    /// <summary>Registers one health check per dependency the replica needs to do its work.</summary>
    public static IServiceCollection AddOperations(this IServiceCollection services, HubConfiguration configuration)
    {
        services.AddSingleton<DrainState>();
        services.AddHealthChecks()
            .AddCheck(WikiRepositoryCheck, () => CheckWikiRepository(configuration.WikiRepositoryPath))
            .AddCheck(StateDatabaseCheck, () => CheckStateDatabase(configuration.StateDatabasePath))
            .AddCheck(EgressCheck, () => CheckEgress(configuration.ModelBaseUrl));

        return services;
    }

    /// <summary>
    /// Maps liveness and readiness. Liveness answers "is the process up and serving" — the
    /// orchestrator restarts it when it is not. Readiness answers "should this replica take
    /// traffic", and is the surface for the <c>grimoire.hub.readiness</c> signal (plan IV).
    /// </summary>
    /// <remarks>
    /// The checks themselves are the framework's own registrations, run through
    /// <see cref="HealthCheckService"/> (constitution VII.1). The two routes are ordinary
    /// endpoints rather than <c>MapHealthChecks</c> so they appear in the document the hub serves
    /// — the contract drift test covers the whole served surface, which is why both live in
    /// <c>hub-api.openapi.yaml</c> under the <c>Operations</c> tag (plan V.5).
    /// </remarks>
    public static WebApplication MapOperations(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new HealthBody("healthy")))
            .WithName("liveness")
            .WithTags("Operations")
            .WithSummary("Is the process up and serving?")
            .Produces<HealthBody>(StatusCodes.Status200OK);

        app.MapGet("/readyz", async (
                HealthCheckService health, DrainState drain, ILoggerFactory loggers, HttpContext context) =>
            {
                var report = await health.CheckHealthAsync(context.RequestAborted);
                var draining = drain.IsDraining;
                var ready = !draining && report.Status is HealthStatus.Healthy;

                string Check(string name) =>
                    report.Entries.TryGetValue(name, out var entry) && entry.Status is HealthStatus.Healthy
                        ? "ok"
                        : "failed";

                var body = new ReadinessBody(
                    ready ? "ready" : "not-ready",
                    draining,
                    new ReadinessChecks(Check(WikiRepositoryCheck), Check(StateDatabaseCheck), Check(EgressCheck)));

                // The signal and its surface are one answer: logged from the same report the body
                // is built from, so the two cannot disagree (plan IV, grimoire.hub.readiness).
                loggers.CreateLogger("Grimoire.Hub.Operations").LogInformation(
                    "grimoire.hub.readiness {Status} {Draining} {WikiRepo} {StateDb} {Egress}",
                    body.Status,
                    draining,
                    body.Checks.WikiRepo,
                    body.Checks.StateDb,
                    body.Checks.Egress);

                return ready
                    ? Results.Ok(body)
                    : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("readiness")
            .WithTags("Operations")
            .WithSummary("Should this replica take traffic?")
            .Produces<ReadinessBody>(StatusCodes.Status200OK)
            .Produces<ReadinessBody>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static HealthCheckResult CheckWikiRepository(string path)
    {
        try
        {
            var head = new GitCli(path).RevParseHead();
            return head.Length is 40
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"'{path}' did not report a commit.");
        }
        catch (Exception exception) when (exception is GitCommandFailedException or IOException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy($"'{path}' is not a reachable wiki repository: {exception.Message}");
        }
    }

    private static HealthCheckResult CheckStateDatabase(string path)
    {
        try
        {
            using var store = new SqliteStore(path);
            store.EnsureSchema();
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy($"'{path}' is not a writable state database: {exception.Message}");
        }
    }

    private static HealthCheckResult CheckEgress(string modelBaseUrl) =>
        // Readiness asks whether the one permitted destination is reachable, so "the egress path
        // is broken" stays distinguishable from "the agent failed" (plan IV).
        ModelEndpointProbe.Unreachable(modelBaseUrl, TimeSpan.FromSeconds(2)) is { } reason
            ? HealthCheckResult.Unhealthy(reason)
            : HealthCheckResult.Healthy();

    /// <summary>The <c>/healthz</c> body, as the contract describes it.</summary>
    public sealed record HealthBody(string Status);

    /// <summary>
    /// The <c>/readyz</c> body, as the contract describes it: the surface for
    /// <c>grimoire.hub.readiness</c>.
    /// </summary>
    public sealed record ReadinessBody(string Status, bool Draining, ReadinessChecks Checks);

    /// <summary>
    /// One field per dependency, named as the contract names them — a fixed shape rather than a
    /// dictionary, so the served document says which checks exist and the drift test can hold it to
    /// the contract.
    /// </summary>
    public sealed record ReadinessChecks(string WikiRepo, string StateDb, string Egress);
}

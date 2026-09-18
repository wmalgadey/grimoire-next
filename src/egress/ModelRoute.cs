using System.Security.Cryptography;
using System.Text;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Grimoire.Egress;

/// <summary>
/// The model route (ADR-0010, ADR-0012): <c>/v1/*</c> to the single configured upstream, with the
/// caller's opaque internal token checked and removed and the upstream credential attached.
/// </summary>
public static class ModelRoute
{
    private const string RouteId = "model";
    private const string ClusterId = "model-upstream";

    /// <summary>The per-run correlation header; logged here, never sent upstream.</summary>
    public const string RunHeader = "X-Grimoire-Run";

    /// <summary>The route and its one-destination cluster.</summary>
    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Config(EgressConfiguration configuration) =>
    (
        [
            new RouteConfig
            {
                RouteId = RouteId,
                ClusterId = ClusterId,
                Match = new RouteMatch { Path = "/v1/{**rest}" },
            },
        ],
        [
            new ClusterConfig
            {
                ClusterId = ClusterId,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["upstream"] = new() { Address = configuration.ModelUpstream.ToString() },
                },
                HttpRequest = new ForwarderRequestConfig
                {
                    // Streamed model responses pass through as they arrive. Buffering is off by
                    // default; saying so here keeps it off when a default changes.
                    AllowResponseBuffering = false,
                    // An agent turn can think for a long time between bytes.
                    ActivityTimeout = TimeSpan.FromMinutes(10),
                },
            },
        ]
    );

    /// <summary>Strips what the caller presented and attaches what the upstream expects.</summary>
    public static void Transforms(TransformBuilderContext context)
    {
        // No X-Forwarded-*: the upstream has no use for the deployment's internal addresses.
        context.UseDefaultForwarders = false;

        var credentials = context.Services.GetRequiredService<CredentialProvider>();
        context.AddRequestTransform(transform =>
        {
            var headers = transform.ProxyRequest.Headers;
            headers.Remove("Authorization");
            headers.Remove("Proxy-Authorization");
            headers.Remove("x-api-key");
            headers.Remove(RunHeader);
            var (name, value) = credentials.Current().AsHeader();
            headers.TryAddWithoutValidation(name, value);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Refuses a model request that does not carry the internal token. A caller without it gets
    /// nothing forwarded — the credential is attached only to requests from inside the deployment.
    /// </summary>
    public static Func<HttpContext, Func<Task>, Task> RequireInternalToken(string internalToken)
    {
        var expected = Encoding.UTF8.GetBytes($"Bearer {internalToken}");
        return async (context, next) =>
        {
            var presented = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
            if (!CryptographicOperations.FixedTimeEquals(presented, expected))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        };
    }
}

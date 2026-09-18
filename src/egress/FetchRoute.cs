using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace Grimoire.Egress;

/// <summary>
/// The fetch route (ADR-0010, ADR-0012): <c>GET /fetch?url=…</c> forwarded to that one URL, after
/// the destination policy, carrying no credential and none of the caller's headers.
/// </summary>
/// <remarks>
/// The caller addresses the route explicitly rather than using the proxy as a forward proxy: an
/// https destination through a forward proxy is a <c>CONNECT</c> tunnel, which the hosting server
/// does not hand to an application, and which would hide the request from the policy anyway.
/// Redirects are returned to the caller, not followed, so every hop comes back through here.
/// </remarks>
public static class FetchRoute
{
    /// <summary>The path the hub's <c>GRIMOIRE_FETCH_PROXY</c> names.</summary>
    public const string Path = "/fetch";

    /// <summary>Why a retrieval was refused or failed here, readable by the hub and the operator.</summary>
    public const string ReasonHeader = "X-Grimoire-Egress-Reason";

    /// <summary>
    /// The connection pool for retrievals. Its connect step is the destination policy; it follows
    /// no redirect, keeps no cookie, uses no proxy, and propagates no trace header.
    /// </summary>
    public static HttpMessageInvoker CreateInvoker() => new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ActivityHeadersPropagator = null,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = DestinationPolicy.ConnectToAPublicAddress,
    });

    /// <summary>Builds the outgoing request: the target URL, and nothing from the caller.</summary>
    public static HttpTransformer Transformer(Uri target) => new TargetOnly(target);

    /// <summary>Serves one retrieval.</summary>
    public static async Task Handle(HttpContext context, IHttpForwarder forwarder, HttpMessageInvoker invoker)
    {
        var requested = context.Request.Query["url"].ToString();
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https"))
        {
            Refuse(context, StatusCodes.Status400BadRequest, $"'{requested}' is not an http or https URL.");
            return;
        }

        var error = await forwarder.SendAsync(
            context,
            target.GetLeftPart(UriPartial.Authority),
            invoker,
            new ForwarderRequestConfig
            {
                AllowResponseBuffering = false,
                ActivityTimeout = TimeSpan.FromSeconds(30),
            },
            Transformer(target));

        if (error is ForwarderError.None || context.Response.HasStarted)
        {
            return;
        }

        var exception = context.GetForwarderErrorFeature()?.Exception;
        if (Find<DestinationRefusedException>(exception) is { } refused)
        {
            Refuse(context, StatusCodes.Status403Forbidden, refused.Message);
            return;
        }

        // Left as the forwarder's 502 or 504, with the reason alongside it.
        context.Response.Headers[ReasonHeader] = OneLine($"{target.Host}: {Innermost(exception)?.Message ?? error.ToString()}");
    }

    private static void Refuse(HttpContext context, int status, string reason)
    {
        context.Response.StatusCode = status;
        context.Response.Headers[ReasonHeader] = OneLine(reason);
    }

    private static T? Find<T>(Exception? exception) where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static Exception? Innermost(Exception? exception)
    {
        while (exception?.InnerException is not null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    // A header value is one line of visible ASCII.
    private static string OneLine(string text) =>
        new(text.Select(c => c is >= ' ' and <= '~' ? c : ' ').ToArray());

    private sealed class TargetOnly(Uri target) : HttpTransformer
    {
        // Deliberately not calling the base, which copies the caller's headers: this route sends
        // the destination nothing it did not ask for, so no credential can ride along.
        public override ValueTask TransformRequestAsync(
            HttpContext httpContext,
            HttpRequestMessage proxyRequest,
            string destinationPrefix,
            CancellationToken cancellationToken)
        {
            proxyRequest.RequestUri = target;
            proxyRequest.Headers.Host = null;
            return ValueTask.CompletedTask;
        }

        // The origin is user-chosen. The reason header is the proxy's to set, and one an origin
        // sent would reach the task as if the proxy had refused — attacker-controlled text on an
        // operator's surface.
        public override async ValueTask<bool> TransformResponseAsync(
            HttpContext httpContext,
            HttpResponseMessage? proxyResponse,
            CancellationToken cancellationToken)
        {
            var proceed = await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
            httpContext.Response.Headers.Remove(ReasonHeader);
            return proceed;
        }
    }
}

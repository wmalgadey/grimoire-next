using System.Net.Sockets;

namespace Grimoire.Dispatch.Adapters;

/// <summary>
/// Whether the one permitted destination out of the hub — the proxy's model route — is there. A
/// TCP connect, not a model call: it asks whether the egress path exists, which is a different
/// question from whether the agent did its job (plan IV, <c>grimoire.run.model_endpoint_unreachable</c>).
/// </summary>
/// <remarks>
/// Asked in two places — before a run is dispatched, and by <c>/readyz</c> — and answered here
/// once. It is a network call, so it lives in an adapter like every other one (constitution V.3).
/// </remarks>
public static class ModelEndpointProbe
{
    /// <summary>
    /// Why the endpoint cannot be reached, in words an operator can act on, or <c>null</c> when it
    /// can. Never throws: an endpoint that cannot even be parsed is a reason, not an exception.
    /// </summary>
    public static string? Unreachable(string modelBaseUrl, TimeSpan timeout)
    {
        if (!Uri.TryCreate(modelBaseUrl, UriKind.Absolute, out var uri))
        {
            return $"The model endpoint '{modelBaseUrl}' is not an absolute URL. Check GRIMOIRE_MODEL_BASE_URL.";
        }

        // A URI can parse as absolute and still have no usable host or TCP port — `file:///tmp/x`
        // does, with an empty host and Port == -1. TcpClient would throw on that synchronously,
        // outside the catch below.
        if (string.IsNullOrEmpty(uri.Host) || uri.Port is < 0 or > 65535)
        {
            return $"The model endpoint '{modelBaseUrl}' has no host and port to connect to. "
                + "Check GRIMOIRE_MODEL_BASE_URL.";
        }

        try
        {
            using var socket = new TcpClient();
            return socket.ConnectAsync(uri.Host, uri.Port).Wait(timeout)
                ? null
                : $"The model endpoint {uri.Host}:{uri.Port} did not answer within {timeout.TotalSeconds:0} seconds.";
        }
        catch (Exception exception) when (exception is SocketException or AggregateException or IOException)
        {
            return $"The model endpoint {uri.Host}:{uri.Port} is unreachable: "
                + $"{(exception as AggregateException)?.InnerException?.Message ?? exception.Message}";
        }
    }
}

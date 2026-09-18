using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Grimoire.Ingest.Adapters;

/// <summary>What retrieval produced, or why it produced nothing.</summary>
/// <param name="Text">The retrieved text, when retrieval succeeded.</param>
/// <param name="Failure">
/// A human-readable reason, recorded as the task's failure reason (FR-003, FR-018). Names the
/// destination and what happened, so an operator can tell a refusal from an outage.
/// </param>
public sealed record RetrievalResult(string? Text, string? Failure)
{
    /// <summary>Whether there is text to hand to a run.</summary>
    public bool Succeeded => Text is not null;
}

/// <summary>
/// Retrieval of a submitted URL. This file is the only place in the repository that uses
/// <see cref="HttpClient"/> — adapter confinement asserted by
/// <c>tests/architecture/AdapterConfinementTests.cs</c> (constitution V.3).
/// </summary>
/// <remarks>
/// URL retrieval is the one place a user's input becomes an outbound request, so it is also the
/// one place server-side request forgery has to be refused (research R16). In a container the
/// request goes to the egress proxy's fetch route, <c>GRIMOIRE_FETCH_PROXY</c>, which enforces the
/// same policy independently — two checks, because the in-process one protects a hub running with
/// no proxy and the proxy's one sees the actual connection (ADR-0010).
///
/// The route is addressed explicitly — <c>GET {GRIMOIRE_FETCH_PROXY}?url=…</c> — not used as a
/// forward proxy: an https destination through a forward proxy is a <c>CONNECT</c> tunnel, which
/// the proxy could neither inspect nor serve.
/// </remarks>
public sealed class UrlFetch(HttpClient httpClient, Uri? fetchRoute)
{
    /// <summary>
    /// The response header in which the fetch route says why it refused or could not retrieve a
    /// destination. Its text becomes the task's failure reason.
    /// </summary>
    public const string ProxyReasonHeader = "X-Grimoire-Egress-Reason";

    /// <summary>
    /// Whether this client applies the destination policy itself.
    ///
    /// It does when it connects directly — a plain process on a developer machine. It does not
    /// when a fetch route is configured, because then the hub connects to the proxy and never
    /// resolves the submitted host at all; the proxy is what reaches the destination and the proxy
    /// is what refuses it (ADR-0010, TS-21). Checking here as well would refuse every destination
    /// the hub itself cannot route to, which in a container is all of them.
    /// </summary>
    public bool ChecksDestinationItself => fetchRoute is null;

    /// <summary>
    /// Builds a client that goes through the egress proxy's fetch route when there is one, with
    /// redirects capped and each hop re-checked rather than trusted.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="fetchRoute"/> is not an absolute http(s) URL.</exception>
    public static UrlFetch Create(string? fetchRoute)
    {
        Uri? route = null;
        if (!string.IsNullOrWhiteSpace(fetchRoute)
            && (!Uri.TryCreate(fetchRoute, UriKind.Absolute, out route) || route.Scheme is not ("http" or "https")))
        {
            throw new ArgumentException($"GRIMOIRE_FETCH_PROXY must be an absolute http(s) URL; it is '{fetchRoute}'.");
        }

        // A plain HttpClientHandler resolves the hostname once for Refuse(uri) below and again,
        // independently, for the actual connection — a DNS-rebinding host can answer the first
        // lookup with a public address and the second with a loopback or private one, passing the
        // check and reaching the destination it was refused. SocketsHttpHandler's ConnectCallback
        // makes resolution and the policy check the same lookup as the connection itself, so there
        // is no second answer for a rebinding host to give (research R16).
        var handler = new SocketsHttpHandler
        {
            // Redirects are followed by hand below so every hop passes the same policy: an origin
            // that redirects to 169.254.169.254 must not be followed there.
            AllowAutoRedirect = false,
            UseProxy = false,
            // Through the fetch route the hub connects to the proxy — on the deployment's private
            // network, which this policy would refuse — and never resolves the submitted host at
            // all; the proxy is what reaches the destination and the proxy is what refuses it.
            ConnectCallback = route is null ? ConnectToACheckedAddress : null,
        };

        return new UrlFetch(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) }, route);
    }

    /// <summary>
    /// The connection SocketsHttpHandler actually opens, resolving and checking the destination in
    /// the one step that also makes the connection — closing the gap between "checked" and
    /// "connected to" that a separate resolution would leave open.
    /// </summary>
    private static async ValueTask<System.IO.Stream> ConnectToACheckedAddress(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            throw new HttpRequestException($"{host} could not be resolved.", exception);
        }

        if (addresses.Length is 0)
        {
            throw new HttpRequestException($"{host} resolved to no address.");
        }

        // Every address, not just the first: a host that resolves to one public and one private
        // address is a way to get a private one connected to.
        foreach (var address in addresses)
        {
            if (IsNotRoutableFromHere(address))
            {
                throw new HttpRequestException(
                    $"{host} resolves to {address}, which is not a destination this system retrieves.");
            }
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The most redirects a retrieval will follow before giving up.</summary>
    public const int MaxRedirects = 5;

    /// <summary>
    /// Retrieves the text at a submitted URL, or says why it did not. Never throws for a bad URL,
    /// an unreachable host, an error response, or a refused destination: each of those is a task
    /// failure reason, not an exception for the caller to interpret.
    /// </summary>
    public async Task<RetrievalResult> Retrieve(string submittedUrl, CancellationToken cancellationToken)
    {
        var current = submittedUrl;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!Uri.TryCreate(current, UriKind.Absolute, out var uri))
            {
                return Failed($"'{current}' is not a URL that can be retrieved.");
            }

            if (uri.Scheme is not ("http" or "https"))
            {
                return Failed(
                    $"'{uri.Scheme}' is not a scheme this system retrieves. Submit an http or https URL.");
            }

            if (ChecksDestinationItself && Refuse(uri) is { } refusal)
            {
                return Failed(refusal);
            }

            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(Through(uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or SocketException)
            {
                return Failed($"{uri} could not be reached: {exception.Message}");
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return Failed($"{uri} answered {(int)response.StatusCode} with no destination to follow.");
                    }

                    current = location.IsAbsoluteUri ? location.ToString() : new Uri(uri, location).ToString();
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Failed(Unsuccessful(uri, response));
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!IsText(mediaType))
                {
                    return Failed(
                        $"{uri} answered with content type '{mediaType ?? "unknown"}'. Only text can be ingested.");
                }

                // No size limit and no truncation (FR-029): the source is handed to the run
                // whole. A hard cap here would not even buy memory safety — the body is already
                // fully read by the time any cap could be checked — so it would only be a reason
                // to refuse a source the spec says must be accepted.
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return new RetrievalResult(Encoding.UTF8.GetString(bytes), null);
            }
        }

        return Failed($"'{submittedUrl}' redirected more than {MaxRedirects} times.");
    }

    /// <summary>
    /// Why this destination is refused, or <c>null</c> when it is allowed. A refusal is a policy
    /// decision, not a connection failure: something listening on loopback is exactly the case a
    /// naive implementation would happily fetch.
    /// </summary>
    public static string? Refuse(Uri uri)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal)
                ? [literal]
                : Dns.GetHostAddresses(uri.Host);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return $"{uri.Host} could not be resolved.";
        }

        if (addresses.Length is 0)
        {
            return $"{uri.Host} resolved to no address.";
        }

        // Every address, not just the first: a host that resolves to one public and one private
        // address is a way to get a private one fetched.
        foreach (var address in addresses)
        {
            if (IsNotRoutableFromHere(address))
            {
                return $"{uri.Host} resolves to {address}, which is not a destination this system retrieves.";
            }
        }

        return null;
    }

    private static bool IsNotRoutableFromHere(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily is AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            // An IPv4-mapped address is an IPv4 address wearing a hat.
            if (address.IsIPv4MappedToIPv6)
            {
                return IsNotRoutableFromHere(address.MapToIPv4());
            }

            // Unique local addresses, fc00::/7.
            var v6 = address.GetAddressBytes();
            return (v6[0] & 0xFE) == 0xFC;
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => true,                                        // "this network"
            10 => true,                                       // 10.0.0.0/8
            127 => true,                                      // loopback
            169 when octets[1] is 254 => true,                // link-local, incl. cloud metadata
            172 when octets[1] is >= 16 and <= 31 => true,     // 172.16.0.0/12
            192 when octets[1] is 168 => true,                // 192.168.0.0/16
            192 when octets[1] is 0 && octets[2] is 0 => true, // IETF protocol assignments
            100 when octets[1] is >= 64 and <= 127 => true,    // carrier-grade NAT
            >= 224 => true,                                   // multicast and reserved
            _ => false,
        };
    }

    /// <summary>
    /// The reason an unsuccessful answer becomes. Through the fetch route that is the proxy's own
    /// account when it gives one — a refused destination reads as a refusal, not as a bare 403.
    /// </summary>
    private string Unsuccessful(Uri uri, HttpResponseMessage response) =>
        fetchRoute is not null
        && response.Headers.TryGetValues(ProxyReasonHeader, out var reasons)
        && string.Join(" ", reasons) is { Length: > 0 } reason
            ? $"{uri} was not retrieved: {reason}"
            : $"{uri} answered {(int)response.StatusCode} {response.ReasonPhrase}.";

    /// <summary>Where the request for <paramref name="uri"/> is actually sent.</summary>
    private Uri Through(Uri uri)
    {
        if (fetchRoute is null)
        {
            return uri;
        }

        var query = $"url={Uri.EscapeDataString(uri.AbsoluteUri)}";
        return new UriBuilder(fetchRoute)
        {
            Query = string.IsNullOrEmpty(fetchRoute.Query) ? query : $"{fetchRoute.Query.TrimStart('?')}&{query}",
        }.Uri;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static bool IsText(string? mediaType) =>
        mediaType is not null
        && (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase));

    private static RetrievalResult Failed(string reason) => new(null, reason);
}

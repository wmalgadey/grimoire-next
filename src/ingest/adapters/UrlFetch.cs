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
/// request goes through <c>GRIMOIRE_FETCH_PROXY</c>, which enforces the same policy independently
/// — two checks, because the in-process one gives the operator a readable reason and the network
/// one makes the deny-all posture real (ADR-0010).
/// </remarks>
public sealed class UrlFetch(HttpClient httpClient, bool checksDestinationItself)
{
    /// <summary>
    /// Whether this client applies the destination policy itself.
    ///
    /// It does when it connects directly — a plain process on a developer machine. It does not
    /// when a fetch proxy is configured, because then the hub connects to the proxy and never
    /// resolves the submitted host at all; the proxy is what reaches the destination and the proxy
    /// is what refuses it (ADR-0010, TS-21). Checking here as well would refuse every destination
    /// the hub itself cannot route to, which in a container is all of them.
    /// </summary>
    public bool ChecksDestinationItself { get; } = checksDestinationItself;

    /// <summary>
    /// Builds a client routed through the egress proxy when there is one, with redirects capped
    /// and each hop re-checked rather than trusted.
    /// </summary>
    public static UrlFetch Create(string? fetchProxy)
    {
        var handler = new HttpClientHandler
        {
            // Redirects are followed by hand below so every hop passes the same policy: an origin
            // that redirects to 169.254.169.254 must not be followed there.
            AllowAutoRedirect = false,
        };

        if (!string.IsNullOrWhiteSpace(fetchProxy))
        {
            handler.Proxy = new WebProxy(fetchProxy);
            handler.UseProxy = true;
        }

        return new UrlFetch(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) },
            checksDestinationItself: string.IsNullOrWhiteSpace(fetchProxy));
    }

    /// <summary>The most redirects a retrieval will follow before giving up.</summary>
    public const int MaxRedirects = 5;

    /// <summary>The largest response the retrieval will read. Generous: there is no source-size limit.</summary>
    private const int MaxResponseBytes = 64 * 1024 * 1024;

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
                response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
                    return Failed($"{uri} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!IsText(mediaType))
                {
                    return Failed(
                        $"{uri} answered with content type '{mediaType ?? "unknown"}'. Only text can be ingested.");
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length > MaxResponseBytes)
                {
                    return Failed($"{uri} answered with {bytes.Length} bytes, more than this system will retrieve.");
                }

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

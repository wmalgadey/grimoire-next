using System.Net;
using System.Net.Sockets;

namespace Grimoire.Egress;

/// <summary>A retrieval destination the policy refuses. Carries the reason an operator reads.</summary>
public sealed class DestinationRefusedException(string reason) : HttpRequestException(reason);

/// <summary>
/// The fetch route's destination policy: nothing but public addresses (ADR-0010).
/// </summary>
/// <remarks>
/// The hub applies the same policy in-process when it runs without this proxy. The duplication is
/// the point — this copy protects against a bug in that one, and it sees the actual connection,
/// which a check before connecting does not. It is applied <b>in the connect step itself</b>:
/// resolve, check every address, connect to a checked one, so a DNS-rebinding host has no second
/// lookup to answer differently.
/// </remarks>
public static class DestinationPolicy
{
    /// <summary>
    /// The connection the fetch route's handler opens. Refuses before any socket is connected.
    /// </summary>
    public static async ValueTask<Stream> ConnectToAPublicAddress(
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

        // Every address, not just the first: one public and one private answer is a way to get the
        // private one connected to.
        foreach (var address in addresses)
        {
            if (IsInward(address))
            {
                throw new DestinationRefusedException(
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

    /// <summary>Loopback, link-local, private, and reserved: everything that is not the internet.</summary>
    public static bool IsInward(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily is AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsInward(address.MapToIPv4());
            }

            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || address.Equals(IPAddress.IPv6None))
            {
                return true;
            }

            // Unique local addresses, fc00::/7.
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
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
}

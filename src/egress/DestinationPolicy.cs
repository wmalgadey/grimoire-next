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

        // Every checked address in turn, not just the first: a dual-stack host whose first answer
        // is unreachable from here still has a usable one behind it.
        var failures = new List<Exception>();
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                failures.Add(exception);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException($"{host} could not be connected to.", new AggregateException(failures));
    }

    /// <summary>
    /// Everything that is not the public internet: loopback, link-local, private, shared, reserved,
    /// documentation and benchmarking ranges, and IPv6 outside global unicast — including the forms
    /// that carry an IPv4 address inside them (NAT64, 6to4, Teredo), which could carry a private one.
    /// </summary>
    public static bool IsInward(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsInward(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily is AddressFamily.InterNetworkV6
            // Only global unicast is the internet — which also rules out loopback, unspecified,
            // link-local, unique local, multicast and NAT64 — less the special-use blocks inside it.
            ? !Within(bytes, GlobalUnicast) || InwardV6.Any(block => Within(bytes, block))
            : InwardV4.Any(block => Within(bytes, block));
    }

    private static readonly (byte[] Network, int PrefixLength) GlobalUnicast = Block("2000::", 3);

    private static readonly (byte[] Network, int PrefixLength)[] InwardV6 =
    [
        Block("2001::", 23),       // IETF protocol assignments, incl. Teredo
        Block("2001:db8::", 32),   // documentation
        Block("2002::", 16),       // 6to4, which carries an IPv4 address inside it
    ];

    private static readonly (byte[] Network, int PrefixLength)[] InwardV4 =
    [
        Block("0.0.0.0", 8),        // "this network"
        Block("10.0.0.0", 8),       // private
        Block("100.64.0.0", 10),    // carrier-grade NAT
        Block("127.0.0.0", 8),      // loopback
        Block("169.254.0.0", 16),   // link-local, incl. cloud metadata
        Block("172.16.0.0", 12),    // private
        Block("192.0.0.0", 24),     // IETF protocol assignments
        Block("192.0.2.0", 24),     // TEST-NET-1
        Block("192.88.99.0", 24),   // 6to4 relay anycast
        Block("192.168.0.0", 16),   // private
        Block("198.18.0.0", 15),    // benchmarking
        Block("198.51.100.0", 24),  // TEST-NET-2
        Block("203.0.113.0", 24),   // TEST-NET-3
        Block("224.0.0.0", 3),      // multicast, reserved, broadcast
    ];

    private static (byte[] Network, int PrefixLength) Block(string network, int prefixLength) =>
        (IPAddress.Parse(network).GetAddressBytes(), prefixLength);

    private static bool Within(byte[] address, (byte[] Network, int PrefixLength) block)
    {
        if (address.Length != block.Network.Length)
        {
            return false;
        }

        var whole = block.PrefixLength / 8;
        var rest = block.PrefixLength % 8;
        var mask = (byte)(0xFF << (8 - rest));
        return address.AsSpan(0, whole).SequenceEqual(block.Network.AsSpan(0, whole))
            && (rest is 0 || (address[whole] & mask) == (block.Network[whole] & mask));
    }
}

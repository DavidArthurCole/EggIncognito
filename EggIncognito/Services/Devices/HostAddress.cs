using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EggIncognito.Services.Devices;

// Resolves the LAN IPv4 address devices dial back to reach the per-device capture listeners. The operator
// can pin it via config (DeviceCapture:HostIp); otherwise it auto-detects the host's primary routable IPv4.
// Auto-detect is best-effort: enumerate up, non-loopback, non-virtual interfaces and pick the first private
// IPv4. The pure Pick(...) overload takes candidates so it is unit-testable without real NICs.
public static class HostAddress
{
    // A network interface candidate, reduced to what selection needs.
    public sealed record Nic(string Name, bool IsUp, bool IsLoopback, IReadOnlyList<string> IPv4Addresses);

    // config override wins; else auto-detect; else null (caller logs + the device push is skipped).
    public static string? Resolve(string? configured, IReadOnlyList<Nic>? nics = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return Pick(nics ?? Enumerate());
    }

    // Pure selection over candidates. Prefers a private LAN IPv4 (192.168/10/172.16-31), then any non-
    // loopback IPv4. Skips down/loopback NICs and obvious virtual adapters (docker/veth/vmnet/lo).
    public static string? Pick(IReadOnlyList<Nic> nics)
    {
        var usable = nics
            .Where(n => n.IsUp && !n.IsLoopback && !IsVirtual(n.Name))
            .SelectMany(n => n.IPv4Addresses)
            .Where(IsRoutableV4)
            .ToList();
        return usable.FirstOrDefault(IsPrivate) ?? usable.FirstOrDefault();
    }

    private static bool IsVirtual(string name)
    {
        var n = name.ToLowerInvariant();
        return n.StartsWith("docker") || n.StartsWith("veth") || n.StartsWith("br-")
            || n.StartsWith("vmnet") || n.StartsWith("vbox") || n == "lo" || n.StartsWith("tun")
            || n.StartsWith("tap") || n.StartsWith("wg");
    }

    private static bool IsRoutableV4(string ip) =>
        IPAddress.TryParse(ip, out var a)
        && a.AddressFamily == AddressFamily.InterNetwork
        && !IPAddress.IsLoopback(a)
        && !ip.StartsWith("169.254."); // link-local APIPA

    private static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = a.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    private static IReadOnlyList<Nic> Enumerate()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Select(ni => new Nic(
                    ni.Name,
                    ni.OperationalStatus == OperationalStatus.Up,
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                    ni.GetIPProperties().UnicastAddresses
                        .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(u => u.Address.ToString())
                        .ToList()))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EggIncognito.Services.Devices;

public static class HostAddress {
    public static string? Resolve(string? configured, IReadOnlyList<Nic>? nics = null) =>
        !string.IsNullOrWhiteSpace(configured) ? configured.Trim() : Pick(nics ?? Enumerate());

    public static string? Pick(IReadOnlyList<Nic> nics) {
        var usable = nics
            .Where(n => n.IsUp && !n.IsLoopback && !IsVirtual(n.Name))
            .SelectMany(n => n.IPv4Addresses)
            .Where(IsRoutableV4)
            .ToList();
        return usable.FirstOrDefault(IsPrivate) ?? usable.FirstOrDefault();
    }

    private static bool IsVirtual(string name) {
        string n = name.ToLowerInvariant();
        return n.StartsWith("docker", StringComparison.Ordinal) || n.StartsWith("veth", StringComparison.Ordinal) ||
               n.StartsWith("br-", StringComparison.Ordinal)
               || n.StartsWith("vmnet", StringComparison.Ordinal) || n.StartsWith("vbox", StringComparison.Ordinal) ||
               n == "lo" || n.StartsWith("tun", StringComparison.Ordinal)
               || n.StartsWith("tap", StringComparison.Ordinal) || n.StartsWith("wg", StringComparison.Ordinal);
    }

    private static bool IsRoutableV4(string ip) =>
        IPAddress.TryParse(ip, out var a)
        && a.AddressFamily == AddressFamily.InterNetwork
        && !IPAddress.IsLoopback(a)
        && !ip.StartsWith("169.254.", StringComparison.Ordinal);

    private static bool IsPrivate(string ip) {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = a.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168);
    }

    private static List<Nic> Enumerate() {
        try {
            return [
                .. NetworkInterface.GetAllNetworkInterfaces()
                    .Select(ni => new Nic(
                        ni.Name,
                        ni.OperationalStatus == OperationalStatus.Up,
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                        ni.GetIPProperties().UnicastAddresses
                            .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                            .Select(u => u.Address.ToString())
                            .ToList()))
            ];
        } catch {
            return [];
        }
    }

    public sealed record Nic(string Name, bool IsUp, bool IsLoopback, IReadOnlyList<string> IPv4Addresses);
}

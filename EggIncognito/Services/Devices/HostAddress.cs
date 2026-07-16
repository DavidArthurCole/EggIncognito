using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EggIncognito.Services.Devices;

public static class HostAddress
{
    public sealed record Nic(string Name, bool IsUp, bool IsLoopback, IReadOnlyList<string> IPv4Addresses);

    public static string? Resolve(string? configured, IReadOnlyList<Nic>? nics = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return Pick(nics ?? Enumerate());
    }

   
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
        && !ip.StartsWith("169.254.");

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

using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Capture;

public sealed record HostedCaptureOptions(
    int FrontDoorPort,
    int PortPoolBase,
    int MaxConcurrentSessions,
    int MaxIdleMinutes,
    int MaxSessionHours,
    IReadOnlyList<string> ExtraAllowedHosts,
    string PublicHost,
    string Ipv6Prefix,
    string AddressSecret) {
    public static HostedCaptureOptions Defaults() => new(
        FrontDoorPort: 8443,
        PortPoolBase: 24000,
        MaxConcurrentSessions: 10,
        MaxIdleMinutes: 30,
        MaxSessionHours: 4,
        ExtraAllowedHosts: [],
        PublicHost: "capture.davidarthurcole.me",
        Ipv6Prefix: "2a01:4f8:c012:e15b::/64",
        AddressSecret: "");

    public static HostedCaptureOptions Bind(IConfiguration config) {
        var d = Defaults();
        var s = config.GetSection("Capture");
        return new HostedCaptureOptions(
            FrontDoorPort: Int(s["FrontDoorPort"], d.FrontDoorPort),
            PortPoolBase: Int(s["PortPoolBase"], d.PortPoolBase),
            MaxConcurrentSessions: Int(s["MaxConcurrentSessions"], d.MaxConcurrentSessions),
            MaxIdleMinutes: Int(s["MaxIdleMinutes"], d.MaxIdleMinutes),
            MaxSessionHours: Int(s["MaxSessionHours"], d.MaxSessionHours),
            ExtraAllowedHosts: s.GetSection("ExtraAllowedHosts").GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToArray(),
            PublicHost: s["PublicHost"] ?? d.PublicHost,
            Ipv6Prefix: s["Ipv6Prefix"] ?? d.Ipv6Prefix,
            AddressSecret: s["AddressSecret"] ?? d.AddressSecret);
    }

    private static int Int(string? raw, int fallback) => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;


    public bool IsExtraAllowed(string host) {
        foreach (var h in ExtraAllowedHosts) {
            if (host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }
}

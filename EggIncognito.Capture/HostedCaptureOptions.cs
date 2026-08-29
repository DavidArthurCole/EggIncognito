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
    string AddressSecret,
    int MaxLimitedSessions = 50) {
    public static HostedCaptureOptions Defaults() => new(
        8443,
        24000,
        10,
        30,
        4,
        [],
        "capture.davidarthurcole.me",
        "2a01:4f8:c012:e15b::/64",
        "");

    public static HostedCaptureOptions Bind(IConfiguration config) {
        var d = Defaults();
        var s = config.GetSection("Capture");
        return new HostedCaptureOptions(
            Int(s["FrontDoorPort"], d.FrontDoorPort),
            Int(s["PortPoolBase"], d.PortPoolBase),
            Int(s["MaxConcurrentSessions"], d.MaxConcurrentSessions),
            Int(s["MaxIdleMinutes"], d.MaxIdleMinutes),
            Int(s["MaxSessionHours"], d.MaxSessionHours),
            s.GetSection("ExtraAllowedHosts").GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToArray(),
            s["PublicHost"] ?? d.PublicHost,
            s["Ipv6Prefix"] ?? d.Ipv6Prefix,
            s["AddressSecret"] ?? d.AddressSecret,
            Int(s["MaxLimitedSessions"], d.MaxLimitedSessions));
    }

    private static int Int(string? raw, int fallback) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;


    public bool IsExtraAllowed(string host) {
        foreach (string h in ExtraAllowedHosts) {
            if (host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}

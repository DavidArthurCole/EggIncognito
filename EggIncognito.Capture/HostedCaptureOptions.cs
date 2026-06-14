using Microsoft.Extensions.Configuration;

namespace EggIncognito.Capture;

// Hosted-capture knobs, bound from the "Capture" config section over code defaults (the
// RateLimitOptions.Bind pattern). The front door listens on FrontDoorPort; each per-user session
// gets a loopback base port from the pool (PortPoolBase + 3n; Unobtanium derives +1/+2 internally).
public sealed record HostedCaptureOptions(
    int FrontDoorPort,
    int PortPoolBase,
    int MaxConcurrentSessions,
    int MaxIdleMinutes,
    int MaxSessionHours,
    IReadOnlyList<string> ExtraAllowedHosts,
    string PublicHost,
    string Ipv6Prefix,
    string AddressSecret)
{
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

    public static HostedCaptureOptions Bind(IConfiguration config)
    {
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

    private static int Int(string? raw, int fallback) => int.TryParse(raw, out var v) ? v : fallback;

    // Case-insensitive exact or subdomain match against ExtraAllowedHosts. The auxbrain rule itself
    // stays in AuxbrainHosts; this is the operator escape hatch for hosts the game turns out to need.
    public bool IsExtraAllowed(string host)
    {
        foreach (var h in ExtraAllowedHosts)
        {
            if (host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

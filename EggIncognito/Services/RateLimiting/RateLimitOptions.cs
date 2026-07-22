using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.RateLimiting;

public sealed record RateLimit(int PermitLimit, int WindowSeconds, int SegmentsPerWindow);

public sealed record RateLimitOptions(
    bool Enabled,
    IReadOnlyDictionary<string, RateLimit> Tiers,
    IReadOnlyDictionary<string, RateLimit> Policies) {
    public static RateLimitOptions Defaults() => new(
        Enabled: true,
        Tiers: new Dictionary<string, RateLimit> {
            ["Anon"] = new(PermitLimit: 30, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Viewer"] = new(PermitLimit: 120, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Contributor"] = new(PermitLimit: 600, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Supporter"] = new(PermitLimit: 1200, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Keyed"] = new(PermitLimit: 600, WindowSeconds: 60, SegmentsPerWindow: 6),
        },
        Policies: new Dictionary<string, RateLimit> {
            ["Global"] = new(PermitLimit: 300, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Egress"] = new(PermitLimit: 10, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Write"] = new(PermitLimit: 60, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Read"] = new(PermitLimit: 120, WindowSeconds: 60, SegmentsPerWindow: 6),


            ["Fetch"] = new(PermitLimit: 300, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["Data"] = new(PermitLimit: 600, WindowSeconds: 60, SegmentsPerWindow: 6),
            ["DataAnon"] = new(PermitLimit: 1, WindowSeconds: 3600, SegmentsPerWindow: 1),
        });

    public static RateLimitOptions Bind(IConfiguration config) {
        var d = Defaults();
        var section = config.GetSection("RateLimiting");
        var enabled = section.GetValue("Enabled", d.Enabled);
        var tiers = MergeGroup(section.GetSection("Tiers"), d.Tiers);
        var policies = MergeGroup(section.GetSection("Policies"), d.Policies);
        return new RateLimitOptions(enabled, tiers, policies);
    }

    private static IReadOnlyDictionary<string, RateLimit> MergeGroup(
        IConfigurationSection group, IReadOnlyDictionary<string, RateLimit> defaults) {
        var result = new Dictionary<string, RateLimit>(defaults);
        foreach (var key in defaults.Keys) {
            var s = group.GetSection(key);
            if (!s.Exists()) continue;
            var d = defaults[key];
            result[key] = new RateLimit(
                s.GetValue("PermitLimit", d.PermitLimit),
                s.GetValue("WindowSeconds", d.WindowSeconds),
                s.GetValue("SegmentsPerWindow", d.SegmentsPerWindow));
        }
        return result;
    }
}

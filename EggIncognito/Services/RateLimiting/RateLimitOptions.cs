namespace EggIncognito.Services.RateLimiting;

public sealed record RateLimit(int PermitLimit, int WindowSeconds, int SegmentsPerWindow);

public sealed record RateLimitOptions(
    bool Enabled,
    IReadOnlyDictionary<string, RateLimit> Tiers,
    IReadOnlyDictionary<string, RateLimit> Policies) {
    public static RateLimitOptions Defaults() => new(
        true,
        new Dictionary<string, RateLimit> {
            ["Anon"] = new(30, 60, 6),
            ["Viewer"] = new(120, 60, 6),
            ["Contributor"] = new(600, 60, 6),
            ["Supporter"] = new(1200, 60, 6),
            ["Keyed"] = new(600, 60, 6)
        },
        new Dictionary<string, RateLimit> {
            ["Global"] = new(300, 60, 6),
            ["Egress"] = new(10, 60, 6),
            ["Write"] = new(60, 60, 6),
            ["Read"] = new(120, 60, 6),


            ["Fetch"] = new(300, 60, 6),
            ["Data"] = new(600, 60, 6),
            ["DataAnon"] = new(1, 3600, 1)
        });

    public static RateLimitOptions Bind(IConfiguration config) {
        var d = Defaults();
        var section = config.GetSection("RateLimiting");
        bool enabled = section.GetValue("Enabled", d.Enabled);
        var tiers = MergeGroup(section.GetSection("Tiers"), d.Tiers);
        var policies = MergeGroup(section.GetSection("Policies"), d.Policies);
        return new RateLimitOptions(enabled, tiers, policies);
    }

    private static Dictionary<string, RateLimit> MergeGroup(
        IConfigurationSection group, IReadOnlyDictionary<string, RateLimit> defaults) {
        var result = new Dictionary<string, RateLimit>(defaults);
        foreach (string key in defaults.Keys) {
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

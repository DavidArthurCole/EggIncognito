using EggIncognito.Core.Services;

namespace EggIncognito.Capture;

public sealed class LiveVersionStore(string capturePath)
    : JsonListStore<LiveVersion>(capturePath, "live-versions.json") {
    public LiveVersion? Latest(string platform) {
        string key = NormalizePlatform(platform);
        foreach (var v in Load()) {
            if (string.Equals(v.Platform, key, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }


    public void Observe(RinfoHarvester.ObservedVersion v, string nowIso) {
        if (string.IsNullOrEmpty(v.Platform)) return;
        string key = NormalizePlatform(v.Platform);
        Mutate(rows => {
            int idx = rows.FindIndex(r => string.Equals(r.Platform, key, StringComparison.OrdinalIgnoreCase));
            var prev = idx >= 0 ? rows[idx] : null;
            var merged = new LiveVersion(
                key,
                v.Version ?? prev?.Version,
                v.Build ?? prev?.Build,
                v.ClientVersion ?? prev?.ClientVersion,
                nowIso);
            if (idx >= 0) rows[idx] = merged;
            else rows.Add(merged);
        });
    }


    private static string NormalizePlatform(string p) => (p ?? "").Trim().ToLowerInvariant();
}

public sealed record LiveVersion(string Platform, string? Version, string? Build, int? ClientVersion, string LastSeen);

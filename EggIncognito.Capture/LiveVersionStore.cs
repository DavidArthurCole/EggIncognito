using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Capture;

public sealed class LiveVersionStore(string capturePath) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    private string FilePath => Path.Combine(capturePath, "live-versions.json");

    public IReadOnlyList<LiveVersion> Load() {
        try {
            return !File.Exists(FilePath) ? [] : (IReadOnlyList<LiveVersion>)(JsonSerializer.Deserialize<List<LiveVersion>>(File.ReadAllText(FilePath), Json) ?? []);
        } catch {
            return [];
        }
    }

    public LiveVersion? Latest(string platform) {
        var key = NormalizePlatform(platform);
        foreach (var v in Load())
            if (string.Equals(v.Platform, key, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }


    public void Observe(RinfoHarvester.ObservedVersion v, string nowIso) {
        if (string.IsNullOrEmpty(v.Platform)) return;
        var key = NormalizePlatform(v.Platform);
        lock (_gate) {
            var rows = Load().ToList();
            var idx = rows.FindIndex(r => string.Equals(r.Platform, key, StringComparison.OrdinalIgnoreCase));
            var prev = idx >= 0 ? rows[idx] : null;
            var merged = new LiveVersion(
                key,
                v.Version ?? prev?.Version,
                v.Build ?? prev?.Build,
                v.ClientVersion ?? prev?.ClientVersion,
                nowIso);
            if (idx >= 0) rows[idx] = merged; else rows.Add(merged);
            try {
                Directory.CreateDirectory(capturePath);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(rows, Json));
            } catch {
            }
        }
    }


    private static string NormalizePlatform(string p) => (p ?? "").Trim().ToLowerInvariant();
}
public sealed record LiveVersion(string Platform, string? Version, string? Build, int? ClientVersion, string LastSeen);

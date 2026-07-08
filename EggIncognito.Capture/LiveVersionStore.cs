using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Capture;

// Persists the latest live app version observed on the wire, per platform, to
// <capturePath>/live-versions.json. Source = BasicRequestInfo (rinfo) harvested from captured requests;
// this is the authoritative iOS clientVersion + build, since the static binary cannot give them.
// Best-effort like DeviceStore: missing/corrupt reads empty, write failures swallowed.
public sealed class LiveVersionStore(string capturePath)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    private string FilePath => Path.Combine(capturePath, "live-versions.json");

    public IReadOnlyList<LiveVersion> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<LiveVersion>>(File.ReadAllText(FilePath), Json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public LiveVersion? Latest(string platform)
    {
        var key = NormalizePlatform(platform);
        foreach (var v in Load())
            if (string.Equals(v.Platform, key, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    // Newer non-null fields win; a prior clientVersion/build/version is kept when the new one is null.
    public void Observe(RinfoHarvester.ObservedVersion v, string nowIso)
    {
        if (string.IsNullOrEmpty(v.Platform)) return;
        var key = NormalizePlatform(v.Platform);
        lock (_gate)
        {
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
            try
            {
                Directory.CreateDirectory(capturePath);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(rows, Json));
            }
            catch
            {
            }
        }
    }

    // Store key is lower-case registry platform ("ios"/"android"), regardless of the wire's "IOS".
    private static string NormalizePlatform(string p) => (p ?? "").Trim().ToLowerInvariant();
}

// Latest live version seen for a platform. Build is the auxbrain build the client reports on the wire, not the iOS bundle build.
public sealed record LiveVersion(string Platform, string? Version, string? Build, int? ClientVersion, string LastSeen);

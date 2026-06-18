using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Capture;

// Latest BasicRequestInfo (rinfo) observed on the wire for a single device, keyed by device id. This is
// the per-device sibling of LiveVersionStore (which keys by platform): persistent per-device capture points
// each device at its own listener, so a harvested flow maps to exactly one device. The auxbrain build the
// client reports here is the authoritative iOS build (the static IPA binary cannot give it).
//
// Best-effort like LiveVersionStore/DeviceStore: missing/corrupt reads empty, write failures swallowed so a
// capture is never broken by disk. Upsert keeps prior non-null fields when a newer (thinner) observation
// omits them.
public sealed class DeviceRinfoStore(string capturePath)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    private string FilePath => Path.Combine(capturePath, "device-rinfo.json");

    public IReadOnlyList<DeviceRinfo> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<DeviceRinfo>>(File.ReadAllText(FilePath), Json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public DeviceRinfo? Latest(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        foreach (var v in Load())
            if (string.Equals(v.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    // Upsert the observation for a device. nowIso is stamped by the caller (no clock in the store). Newer
    // non-null fields win; a prior version/build/clientVersion is kept when the new observation is null.
    public void Observe(string deviceId, RinfoHarvester.ObservedVersion v, string nowIso)
    {
        if (string.IsNullOrEmpty(deviceId)) return;
        lock (_gate)
        {
            var rows = Load().ToList();
            var idx = rows.FindIndex(r => string.Equals(r.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            var prev = idx >= 0 ? rows[idx] : null;
            var merged = new DeviceRinfo(
                deviceId,
                v.Platform is { Length: > 0 } ? v.Platform.ToLowerInvariant() : prev?.Platform,
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
                // best-effort: a failed write must never break the capture.
            }
        }
    }
}

// Latest live rinfo seen for one device. Build is the auxbrain build the client reports on the wire (NOT
// the iOS bundle build). LastSeen is an ISO-8601 timestamp.
public sealed record DeviceRinfo(
    string DeviceId, string? Platform, string? Version, string? Build, int? ClientVersion, string LastSeen);

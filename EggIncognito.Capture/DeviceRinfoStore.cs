using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Capture;

// Latest BasicRequestInfo (rinfo) observed on the wire for a single device, keyed by device id. Per-device
// sibling of LiveVersionStore (which keys by platform); the auxbrain build reported here is the
// authoritative iOS build, since the static IPA binary cannot give it.
//
// Best-effort like LiveVersionStore/DeviceStore: missing/corrupt reads empty, write failures swallowed.
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

    // Newer non-null fields win; a prior version/build/clientVersion is kept when the new observation is null.
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
                // best-effort: a failed write must never break the capture
            }
        }
    }
}

// Latest live rinfo seen for one device. Build is the auxbrain build the client reports on the wire, not the iOS bundle build.
public sealed record DeviceRinfo(
    string DeviceId, string? Platform, string? Version, string? Build, int? ClientVersion, string LastSeen);

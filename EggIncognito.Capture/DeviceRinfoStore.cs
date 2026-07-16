using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Capture;


//

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
               
            }
        }
    }
}
public sealed record DeviceRinfo(
    string DeviceId, string? Platform, string? Version, string? Build, int? ClientVersion, string LastSeen);

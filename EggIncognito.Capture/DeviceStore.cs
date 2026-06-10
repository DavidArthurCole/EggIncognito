using System.Text.Json;

namespace EggIncognito.Capture;

// Persists remembered capture devices to <capturePath>/devices.json so the dashboard can show
// previously-seen devices across runs. Best-effort, like the HAR/CA writes: a missing or corrupt
// file reads as an empty list, and write failures are swallowed (never break a capture over it).
// Caps the file at the most-recently-seen devices so it cannot grow unbounded.
public sealed class DeviceStore(string capturePath)
{
    private const int MaxDevices = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private string FilePath => Path.Combine(capturePath, "devices.json");

    public IReadOnlyList<RememberedDevice> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var list = JsonSerializer.Deserialize<List<RememberedDevice>>(File.ReadAllText(FilePath), Json);
            return list ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<RememberedDevice> devices)
    {
        try
        {
            var capped = devices
                .OrderByDescending(d => d.LastSeen, StringComparer.Ordinal)
                .Take(MaxDevices)
                .ToList();
            Directory.CreateDirectory(capturePath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(capped, Json));
        }
        catch
        {
            // best-effort - a failed write must never break the capture
        }
    }
}

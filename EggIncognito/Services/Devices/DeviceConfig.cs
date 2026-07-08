using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Devices;

// Declares the physical devices this host can probe, bound from the "Devices" + "DevicePolling" config.
// Target is the platform-natural address: adb serial (android) or UDID (ios).
public sealed record DeviceConfig
{
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 30;
    public IReadOnlyList<DeviceEntry> Devices { get; init; } = [];

    public static DeviceConfig Bind(IConfiguration config)
    {
        var poll = config.GetSection("DevicePolling");
        var devices = new List<DeviceEntry>();
        foreach (var d in config.GetSection("Devices").GetChildren())
        {
            var id = d["Id"];
            var target = d["Target"];
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target)) continue;
            devices.Add(new DeviceEntry(
                Id: id,
                Platform: (d["Platform"] ?? "android").ToLowerInvariant(),
                Label: d["Label"] ?? id,
                Target: target,
                Package: string.IsNullOrWhiteSpace(d["Package"]) ? "com.auxbrain.egginc" : d["Package"]!));
        }
        return new DeviceConfig
        {
            Enabled = poll.GetValue("Enabled", true),
            IntervalMinutes = poll.GetValue("IntervalMinutes", 30),
            Devices = devices,
        };
    }
}

public sealed record DeviceEntry(string Id, string Platform, string Label, string Target, string Package);

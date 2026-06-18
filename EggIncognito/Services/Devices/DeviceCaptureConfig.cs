using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Devices;

// Persistent per-device capture config, bound from the "DeviceCapture" section. Off by default: this is a
// device-farm-host capability, independent of the public Hosted/Local gate, so a public-facing host can
// still capture its own wired devices. HostIp is the address devices dial back to; unset => auto-detect the
// host's primary LAN IPv4 (HostAddress.Resolve). Ios* hold the proxy-set/clear ssh command templates +
// reuse the iOS ssh creds the updater/puller already use.
public sealed record DeviceCaptureConfig
{
    public bool Enabled { get; init; }
    public int BasePort { get; init; } = 9100;
    public string? HostIp { get; init; } // null => auto-detect

    // iOS proxy push over ssh. Templates use {host}/{port}. Creds reuse DeviceUpdate:Ios when unset here.
    public string? IosSshHost { get; init; }
    public string IosSshPort { get; init; } = "2222";
    public string? IosSshKeyPath { get; init; }
    public string? IosSetCommand { get; init; }
    public string? IosClearCommand { get; init; }

    public static DeviceCaptureConfig Bind(IConfiguration config)
    {
        var dc = config.GetSection("DeviceCapture");
        var ios = dc.GetSection("Ios");
        var upd = config.GetSection("DeviceUpdate").GetSection("Ios"); // fall back to the updater's ssh creds

        return new DeviceCaptureConfig
        {
            Enabled = dc.GetValue("Enabled", false),
            BasePort = dc.GetValue("BasePort", 9100),
            HostIp = Nz(dc["HostIp"]),
            IosSshHost = Nz(ios["SshHost"]) ?? Nz(upd["SshHost"]),
            IosSshPort = Nz(ios["SshPort"]) ?? Nz(upd["SshPort"]) ?? "2222",
            IosSshKeyPath = Nz(ios["SshKeyPath"]) ?? Nz(upd["SshKeyPath"]),
            IosSetCommand = Nz(ios["SetCommand"]),
            IosClearCommand = Nz(ios["ClearCommand"]),
        };
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

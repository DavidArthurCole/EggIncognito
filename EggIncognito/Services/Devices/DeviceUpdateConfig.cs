using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Devices;

// Master + per-platform switches for zero-touch auto-update, bound from the "DeviceUpdate" section.
// Default OFF everywhere: auto-installing on a device is a mutating action, so a host opts in explicitly
// (only the frame host). iOS stays off until the eggupdate.dylib tweak is proven end-to-end on the phone.
public sealed record DeviceUpdateConfig
{
    public bool Enabled { get; init; }
    public bool Android { get; init; }
    public bool Ios { get; init; }

    public bool EnabledFor(string platform) => platform switch
    {
        "android" => Android,
        "ios" => Ios,
        _ => false,
    };

    public static DeviceUpdateConfig Bind(IConfiguration config)
    {
        var s = config.GetSection("DeviceUpdate");
        return new DeviceUpdateConfig
        {
            Enabled = s.GetValue("Enabled", false),
            Android = s.GetSection("Android").GetValue("Enabled", false),
            Ios = s.GetSection("Ios").GetValue("Enabled", false),
        };
    }
}

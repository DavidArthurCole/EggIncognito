namespace EggIncognito.Core.Services.Devices;

public static class DeviceProbeTimeout {
    public static TimeSpan Value { get; set; } = TimeSpan.FromSeconds(30);
}

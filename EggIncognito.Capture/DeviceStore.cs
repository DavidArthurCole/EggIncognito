namespace EggIncognito.Capture;

public sealed class DeviceStore(string capturePath)
    : JsonListStore<RememberedDevice>(capturePath, "devices.json") {
    private const int MaxDevices = 50;

    public void Save(IEnumerable<RememberedDevice> devices) =>
        Replace(devices
            .OrderByDescending(d => d.LastSeen, StringComparer.Ordinal)
            .Take(MaxDevices));
}

using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class DeviceKeyNames {
    private static readonly Dictionary<string, DeviceKey> ByName = new(StringComparer.OrdinalIgnoreCase) {
        ["back"] = DeviceKey.Back,
        ["home"] = DeviceKey.Home,
        ["recents"] = DeviceKey.Recents,
        ["enter"] = DeviceKey.Enter,
        ["wake"] = DeviceKey.Wake,
        ["sleep"] = DeviceKey.Sleep,
        ["dismiss-keyguard"] = DeviceKey.DismissKeyguard
    };

    public static IReadOnlyCollection<string> All => ByName.Keys;

    public static bool TryParse(string? name, out DeviceKey key) {
        key = DeviceKey.Back;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out key);
    }
}

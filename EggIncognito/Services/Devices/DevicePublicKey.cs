using EggIncognito.Core;

namespace EggIncognito.Services.Devices;

public static class DevicePublicKey {
    public static string For(string realId) => Hashes.Sha256HexShort(realId, 16);
}

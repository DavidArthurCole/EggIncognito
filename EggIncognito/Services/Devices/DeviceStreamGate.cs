using System.Collections.Concurrent;

namespace EggIncognito.Services.Devices;

public static class DeviceStreamGate {
    private static readonly ConcurrentDictionary<string, byte> Open = new(StringComparer.Ordinal);

    public static bool TryEnter(string deviceId) => Open.TryAdd(deviceId, 0);

    public static void Exit(string deviceId) => Open.TryRemove(deviceId, out _);
}

namespace EggIncognito.Core.Services.Devices;

public static class AdbTcp {
    public static bool IsNetworkSerial(string serial) => serial.Contains(':');

    public static bool LooksDisconnected(string stderr) {
        string s = stderr.ToLowerInvariant();
        return s.Contains("not found") || s.Contains("device offline")
            || s.Contains("no devices/emulators found") || s.Contains("device still authorizing")
            || s.Contains("device still connecting") || s.Contains("closed");
    }

    public static bool LooksUnauthorized(string stderr) =>
        stderr.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);

    public static async Task ReviveAsync(IProcessRunner runner, string serial, CancellationToken ct) {
        if (!IsNetworkSerial(serial)) return;
        await runner.RunAsync("adb", ["disconnect", serial], ct);
        await runner.RunAsync("adb", ["connect", serial], ct);
    }
}

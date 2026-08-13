
namespace EggIncognito.Core.Services.Devices;

public sealed class DeviceApkPuller(IProcessRunner runner) {
    public Task<byte[]?> PullArmSplitAsync(string serial, string package, CancellationToken ct) =>
        PullSplitAsync(serial, package, DeviceParsing.SelectArmSplit, ct);

    public Task<byte[]?> PullBaseSplitAsync(string serial, string package, CancellationToken ct) =>
        PullSplitAsync(serial, package, DeviceParsing.SelectBaseSplit, ct);

    private async Task<byte[]?> PullSplitAsync(
        string serial, string package, Func<string, string?> select, CancellationToken ct) {
        var conn = new AdbDeviceConnection(runner, serial);
        var pm = await conn.ShellAsync($"pm path {package}", ct);
        if (pm.ExitCode != 0) return null;
        string? path = select(pm.Stdout);
        return path is null ? null : await conn.PullBytesAsync(path, ct);
    }
}

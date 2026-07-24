using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Runner.Adb;

public interface IAdbClient {
    string DumpsysPackage(string package);
    string PullArmApk(string package, string destPath);
}

public sealed class AdbClient : IAdbClient {
    private readonly AdbDeviceConnection _conn;

    public AdbClient(string target) => _conn = new AdbDeviceConnection(new ProcessRunner(), target);

    public string DumpsysPackage(string package) {
        var r = _conn.ShellAsync($"dumpsys package {package}", CancellationToken.None).GetAwaiter().GetResult();
        return r.Stdout + r.Stderr;
    }

    public string PullArmApk(string package, string destPath) {
        var pm = _conn.ShellAsync($"pm path {package}", CancellationToken.None).GetAwaiter().GetResult();
        var arm = DeviceParsing.SelectArmSplit(pm.Stdout)
            ?? throw new InvalidOperationException($"no arm split found for {package}");
        var bytes = _conn.PullBytesAsync(arm, CancellationToken.None).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"adb pull did not produce a file for {arm}");
        File.WriteAllBytes(destPath, bytes);
        return destPath;
    }
}

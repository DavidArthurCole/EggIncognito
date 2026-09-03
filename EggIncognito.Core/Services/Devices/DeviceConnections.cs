namespace EggIncognito.Core.Services.Devices;

public sealed class AdbDeviceConnection(IProcessRunner runner, string serial) : IDeviceConnection {
    public string Serial => serial;
    public string Platform => "android";

    public bool SupportsExecOut => true;

    private bool IsNetworkSerial => serial.Contains(':');

    private static bool LooksDisconnected(string stderr) {
        string s = stderr.ToLowerInvariant();
        return s.Contains("not found") || s.Contains("device offline")
            || s.Contains("no devices/emulators found") || s.Contains("device still authorizing")
            || s.Contains("device still connecting") || s.Contains("closed");
    }

    private async Task<ProcessResult> RunAsync(string[] args, CancellationToken ct) {
        var r = await runner.RunAsync("adb", args, ct);
        if (r.ExitCode == 0 || !IsNetworkSerial || !LooksDisconnected(r.Stderr)) return r;
        await runner.RunAsync("adb", ["connect", serial], ct);
        return await runner.RunAsync("adb", args, ct);
    }

    private async Task<ProcessBytesResult> RunBytesAsync(string[] args, CancellationToken ct) {
        var r = await runner.RunBytesAsync("adb", args, ct);
        if (r.ExitCode == 0 || !IsNetworkSerial || !LooksDisconnected(r.Stderr)) return r;
        await runner.RunAsync("adb", ["connect", serial], ct);
        return await runner.RunBytesAsync("adb", args, ct);
    }

    public Task<ProcessResult> ShellAsync(string command, CancellationToken ct) =>
        RunAsync(["-s", serial, "shell", command], ct);

    public Task<ProcessBytesResult> ExecOutAsync(string command, CancellationToken ct) =>
        RunBytesAsync(["-s", serial, "exec-out", command], ct);

    public async Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct) {
        string dest = DeviceShell.NewTempPath(".bin");
        try {
            var pull = await RunAsync(["-s", serial, "pull", remotePath, dest], ct);
            return pull.ExitCode != 0 ? null : DeviceShell.ReadTemp(dest);
        } finally {
            DeviceShell.TryDelete(dest);
        }
    }

    public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct) =>
        (await RunAsync(["-s", serial, "push", localPath, remotePath], ct)).ExitCode == 0;
}

public sealed class SshDeviceConnection(IProcessRunner runner, SshEndpoint endpoint) : IDeviceConnection {
    public SshEndpoint Endpoint => endpoint;
    public string Platform => "ios";

    public Task<ProcessResult> ShellAsync(string command, CancellationToken ct) =>
        runner.RunAsync("ssh", endpoint.SshArgs(command), ct);

    public async Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct) {
        string dest = DeviceShell.NewTempPath(".bin");
        try {
            var scp = await runner.RunAsync("scp", endpoint.ScpDownArgs(remotePath, dest), ct);
            return scp.ExitCode != 0 ? null : DeviceShell.ReadTemp(dest);
        } finally {
            DeviceShell.TryDelete(dest);
        }
    }

    public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct) =>
        (await runner.RunAsync("scp", endpoint.ScpUpArgs(localPath, remotePath), ct)).ExitCode == 0;
}

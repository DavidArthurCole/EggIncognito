namespace EggIncognito.Core.Services.Devices;

public sealed class AdbDeviceConnection(IProcessRunner runner, string serial) : IDeviceConnection {
    public string Serial => serial;
    public string Platform => "android";

    public bool SupportsExecOut => true;

    public Task<ProcessResult> ShellAsync(string command, CancellationToken ct) =>
        runner.RunAsync("adb", ["-s", serial, "shell", command], ct);

    public Task<ProcessBytesResult> ExecOutAsync(string command, CancellationToken ct) =>
        runner.RunBytesAsync("adb", ["-s", serial, "exec-out", command], ct);

    public async Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct) {
        string dest = DeviceShell.NewTempPath(".bin");
        try {
            var pull = await runner.RunAsync("adb", ["-s", serial, "pull", remotePath, dest], ct);
            return pull.ExitCode != 0 ? null : DeviceShell.ReadTemp(dest);
        } finally {
            DeviceShell.TryDelete(dest);
        }
    }

    public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct) =>
        (await runner.RunAsync("adb", ["-s", serial, "push", localPath, remotePath], ct)).ExitCode == 0;
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

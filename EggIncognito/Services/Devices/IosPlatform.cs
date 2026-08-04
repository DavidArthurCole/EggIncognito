using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class IosPlatform(
    IDeviceConnectionFactory connections,
    DeviceCaptureConfig config,
    IProcessRunner runner,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IEnumerable<IDeviceProxyConfigurator> proxyConfigurators,
    IEnumerable<IDeviceCaInstaller> caInstallers)
    : DevicePlatformBase(Platforms.Ios, storeCheckers, proxyConfigurators, caInstallers) {
    public override async Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<byte[]>.Unreachable("ios ssh not configured");
        byte[]? bytes = await new IosBinaryPuller(conn).PullBinaryAsync(target.Package, ct);
        return bytes is null
            ? DeviceResult<byte[]>.Error($"could not pull binary for {target.Package}")
            : DeviceResult<byte[]>.Success(bytes);
    }

    public override async Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind,
        string name, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<byte[]>.Unreachable("ios ssh not configured");
        var puller = new IosAssetPuller(conn);
        byte[]? bytes = kind switch {
            DeviceAssetKind.Mesh => await puller.PullOneRpoAsync(target.Package, name, ct),
            DeviceAssetKind.Texture => await puller.PullOneTextureAsync(target.Package, name, ct),
            _ => null
        };
        return bytes is null
            ? DeviceResult<byte[]>.Error($"asset not found: {kind} {name}")
            : DeviceResult<byte[]>.Success(bytes);
    }

    public override async Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target,
        DeviceAssetKind kind, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<IReadOnlyList<string>>.Unreachable("ios ssh not configured");
        var puller = new IosAssetPuller(conn);
        IReadOnlyList<string> names = kind switch {
            DeviceAssetKind.Mesh => await puller.ListRposAsync(target.Package, ct),
            DeviceAssetKind.Texture => await puller.ListTexturesAsync(target.Package, ct),
            _ => []
        };
        return DeviceResult<IReadOnlyList<string>>.Success(names);
    }

    public override Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
        new IosDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);

    public override async Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
            return DeviceResult.Unreachable("ios ssh not configured");
        bool unlocked = await IosEnsureUnlockedAsync(ct);
        return unlocked ? DeviceResult.Success("unlocked") : DeviceResult.Error("could not confirm unlock");
    }

    public override async Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
            return DeviceResult.Unreachable("ios ssh not configured");
        if (connections.Ios() is not { } conn) return DeviceResult.Unreachable("ios ssh not configured");
        const string remote =
            "/bin/sh -c 'for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | " +
            "while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; echo killed'";
        await conn.ShellAsync(remote, ct);
        return DeviceResult.Success("killed");
    }

    public override async Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
        string scriptBody, string? addrOffset, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<ParticleCaptureModel.Model>.Unreachable("ios ssh not configured");
        var model = await new IosParticleCapturer(conn, scriptBody, addrOffset).CaptureAsync(ct);
        return model is null
            ? DeviceResult<ParticleCaptureModel.Model>.Error("particle capture returned no model")
            : DeviceResult<ParticleCaptureModel.Model>.Success(model.Value);
    }

    private async Task<bool?> IosLockstateAsync(CancellationToken ct) {
        if (connections.Ios() is not { } conn) return null;
        var r = await conn.ShellAsync("lockstate", ct);
        return r.Stdout.Contains("locked=1")
            ? true
            : r.Stdout.Contains("locked=0")
                ? false
                : r.ExitCode switch { 10 => true, 0 => false, _ => null };
    }

    private async Task<(bool Ok, string? Note)> IosSendCmdAsync(string cmd, CancellationToken ct) {
        if (connections.Ios() is not { } conn) return (false, "ios ssh not configured");
        string remote = $"/bin/sh -c 'printf %s {cmd} > /tmp/ehp.cmd; chmod 666 /tmp/ehp.cmd; echo sent'";
        var r = await conn.ShellAsync(remote, ct);
        return r.ExitCode == 0
            ? (true, null)
            : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    private async Task<bool> IosEnsureUnlockedAsync(CancellationToken ct, int maxTries = 3) {
        for (int i = 0; i < maxTries; i++) {
            bool? locked = await IosLockstateAsync(ct);
            if (locked == false) return true;
            await IosSendCmdAsync("unlock", ct);
            try {
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
            } catch (OperationCanceledException) {
                return false;
            }
        }

        return await IosLockstateAsync(ct) == false;
    }
}

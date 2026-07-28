using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class IosPlatform(
    IDeviceConnectionFactory connections,
    DeviceCaptureConfig config,
    IProcessRunner runner,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IEnumerable<IDeviceProxyConfigurator> proxyConfigurators,
    IEnumerable<IDeviceCaInstaller> caInstallers,
    ILogger<IosPlatform> logger) : IDevicePlatform {
    private readonly IDeviceStoreChecker? _store = storeCheckers.FirstOrDefault(s => s.Platform == "ios");
    private readonly IDeviceProxyConfigurator? _proxy = proxyConfigurators.FirstOrDefault(p => p.Platform == "ios");
    private readonly IDeviceCaInstaller? _ca = caInstallers.FirstOrDefault(c => c.Platform == "ios");

    public string Platform => "ios";

    public DeviceCapabilities Capabilities =>
        DeviceCapabilities.BinaryPull | DeviceCapabilities.AssetRead | DeviceCapabilities.Probe |
        DeviceCapabilities.StoreUpdate | DeviceCapabilities.Proxy | DeviceCapabilities.CaInstall |
        DeviceCapabilities.AppLifecycle | DeviceCapabilities.ParticleCapture;

    public async Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<byte[]>.Unreachable("ios ssh not configured");
        byte[]? bytes = await new IosBinaryPuller(conn).PullBinaryAsync(target.Package, ct);
        return bytes is null
            ? DeviceResult<byte[]>.Error($"could not pull binary for {target.Package}")
            : DeviceResult<byte[]>.Success(bytes);
    }

    public async Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind, string name,
        CancellationToken ct) {
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

    public async Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target, DeviceAssetKind kind,
        CancellationToken ct) {
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

    public Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
        new IosDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);

    public Task<StoreCheckResult> DriveStoreUpdateAsync(DeviceTarget target, CancellationToken ct,
        Action<string>? progress = null) =>
        _store is null
            ? Task.FromResult(new StoreCheckResult(false, null, null, false, false, "unsupported",
                "no ios store checker"))
            : _store.CheckAndUpdateAsync(target, ct, progress);

    public async Task<DeviceResult> SetProxyAsync(DeviceTarget target, string hostIp, int port, CancellationToken ct) =>
        _proxy is null
            ? DeviceResult.Unsupported("no ios proxy configurator")
            : DeviceResult.From(await _proxy.SetProxyAsync(target, hostIp, port, ct));

    public async Task<DeviceResult> ClearProxyAsync(DeviceTarget target, CancellationToken ct) =>
        _proxy is null
            ? DeviceResult.Unsupported("no ios proxy configurator")
            : DeviceResult.From(await _proxy.ClearProxyAsync(target, ct));

    public async Task<DeviceResult> InstallCaAsync(DeviceTarget target, string caPath, CancellationToken ct) =>
        _ca is null
            ? DeviceResult.Unsupported("no ios ca installer")
            : DeviceResult.From(await _ca.InstallAsync(target, caPath, ct));

    public async Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
            return DeviceResult.Unreachable("ios ssh not configured");
        try {
            string bundle = target.Package;
            string? proc = string.IsNullOrEmpty(config.IosAppProcessName) ? "Egg, Inc." : config.IosAppProcessName;

            if (string.IsNullOrEmpty(config.IosRestartCommand)) {
                bool unlocked = await IosEnsureUnlockedAsync(ct);
                if (!unlocked)
                    logger.LogWarning("device capture: {Id} could not confirm unlock; launching anyway", target.Id);
            }

            string remote = string.IsNullOrEmpty(config.IosRestartCommand)
                ? "/bin/sh -c '" +
                  "for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; sleep 1; " +
                  $"uiopen --bundleid {bundle} 2>&1 | sed \"s/^/diag uiopen: /\"; " +
                  "sleep 3; echo diag ps-after:; " +
                  "if ps ax 2>/dev/null | grep -i egg | grep -v grep; then echo \"diag RESULT: running\"; else echo \"diag RESULT: NOT running\"; fi" +
                  "'"
                : config.IosRestartCommand.Replace("{bundle}", bundle).Replace("{proc}", proc);
            if (connections.Ios() is not { } conn) return DeviceResult.Unreachable("ios ssh not configured");
            var r = await conn.ShellAsync(remote, ct);
            string diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
            bool launched = r.Stdout.Contains("diag RESULT: running");
            logger.LogInformation("device capture: {Id} ios restart (running-after={Ok}): {Diag}", target.Id, launched,
                diag);
            return r.ExitCode == 0
                ? DeviceResult.Success($"{(launched ? "running" : "NOT running - see diag")}: {diag}")
                : DeviceResult.Error(diag);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} app restart failed (non-fatal)", target.Id);
            return DeviceResult.Error(ex.Message);
        }
    }

    public async Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
            return DeviceResult.Unreachable("ios ssh not configured");
        await IosKillAppAsync(ct);
        (bool ok, string? note) = await IosSendCmdAsync("lock", ct);
        return ok ? DeviceResult.Success("app killed + locked") : DeviceResult.Error($"lock failed: {note}");
    }

    public async Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
            return DeviceResult.Unreachable("ios ssh not configured");
        bool unlocked = await IosEnsureUnlockedAsync(ct);
        return unlocked ? DeviceResult.Success("unlocked") : DeviceResult.Error("could not confirm unlock");
    }

    public async Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
            return DeviceResult.Unreachable("ios ssh not configured");
        if (connections.Ios() is not { } conn) return DeviceResult.Unreachable("ios ssh not configured");
        const string remote =
            "/bin/sh -c 'for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | " +
            "while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; echo killed'";
        await conn.ShellAsync(remote, ct);
        return DeviceResult.Success("killed");
    }

    public async Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
        string scriptBody, string? addrOffset, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<ParticleCaptureModel.Model>.Unreachable("ios ssh not configured");
        var model = await new IosParticleCapturer(conn, scriptBody, addrOffset).CaptureAsync(ct);
        return model is null
            ? DeviceResult<ParticleCaptureModel.Model>.Error("particle capture returned no model")
            : DeviceResult<ParticleCaptureModel.Model>.Success(model.Value);
    }

    private async Task IosKillAppAsync(CancellationToken ct) {
        const string remote =
            "/bin/sh -c 'for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | " +
            "while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; echo killed'";
        if (connections.Ios() is { } conn) await conn.ShellAsync(remote, ct);
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

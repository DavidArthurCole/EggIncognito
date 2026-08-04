using System.IO.Compression;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Devices;

public sealed class AndroidPlatform(
    IProcessRunner runner,
    IConfiguration appConfig,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IEnumerable<IDeviceProxyConfigurator> proxyConfigurators,
    IEnumerable<IDeviceCaInstaller> caInstallers,
    ILogger<AndroidPlatform> logger)
    : DevicePlatformBase(Platforms.Android, storeCheckers, proxyConfigurators, caInstallers) {
    private readonly bool _particleCaptureEnabled =
        appConfig.GetValue("DeviceCapture:AndroidParticleCapture", true);

    public override async Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) {
        var puller = new DeviceApkPuller(runner);
        byte[]? apk = await puller.PullArmSplitAsync(target.Target, target.Package, ct)
                      ?? await puller.PullBaseSplitAsync(target.Target, target.Package, ct);
        if (apk is null) {
            logger.LogWarning("android: {Id} could not pull apk split for {Pkg}", target.Id, target.Package);
            return DeviceResult<byte[]>.Unreachable("no apk pulled (device offline or adb unavailable)");
        }

        byte[]? so = ExtractLibFromApk(apk);
        return so is null
            ? DeviceResult<byte[]>.Error("libegginc.so not found inside apk")
            : DeviceResult<byte[]>.Success(so);
    }

    public override async Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind,
        string name, CancellationToken ct) {
        byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(target.Target, target.Package, ct);
        byte[]? asset = apk is null
            ? null
            : kind switch {
                DeviceAssetKind.Mesh => RpoAssetLister.ReadStem(apk, name),
                DeviceAssetKind.Texture => ApkTextureLister.ReadStem(apk, name),
                _ => null
            };
        return asset is null
            ? DeviceResult<byte[]>.Error($"asset '{name}' ({kind}) not found (no apk or missing entry)")
            : DeviceResult<byte[]>.Success(asset);
    }

    public override async Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target,
        DeviceAssetKind kind, CancellationToken ct) {
        byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(target.Target, target.Package, ct);
        if (apk is null) return DeviceResult<IReadOnlyList<string>>.Error("no apk pulled");
        IReadOnlyList<string> stems = kind switch {
            DeviceAssetKind.Mesh => RpoAssetLister.ListStems(apk),
            DeviceAssetKind.Texture => ApkTextureLister.ListStems(apk),
            _ => []
        };
        return DeviceResult<IReadOnlyList<string>>.Success(stems);
    }

    public override Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
        new AdbDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);

    public override async Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) {
        await Adb(target.Target, ["shell", "input", "keyevent", "KEYCODE_WAKEUP"], ct);
        var r = await Adb(target.Target, ["shell", "wm", "dismiss-keyguard"], ct);
        return r.ExitCode == 0 ? DeviceResult.Success("unlocked") : DeviceResult.Error("unlock failed");
    }

    public override async Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) {
        var r = await Adb(target.Target, ["shell", "am", "force-stop", target.Package], ct);
        return r.ExitCode == 0 ? DeviceResult.Success("killed") : DeviceResult.Error("force-stop failed");
    }

    public override async Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
        string scriptBody, string? addrOffset, CancellationToken ct) {
        if (!_particleCaptureEnabled)
            return DeviceResult<ParticleCaptureModel.Model>.Unsupported("android particle capture disabled by config");

        var conn = new AdbDeviceConnection(runner, target.Target);
        var model = await new AndroidParticleCapturer(conn, scriptBody, addrOffset).CaptureAsync(ct);
        return model is { } m
            ? DeviceResult<ParticleCaptureModel.Model>.Success(m)
            : DeviceResult<ParticleCaptureModel.Model>.Error("frida capture failed or frida-server absent");
    }

    private Task<ProcessResult> Adb(string serial, string[] rest, CancellationToken ct) =>
        runner.RunAsync("adb", ["-s", serial, .. rest], ct);

    private static byte[]? ExtractLibFromApk(byte[] apk) {
        if (apk.Length == 0) return null;
        ZipArchive zip;
        try {
            zip = new ZipArchive(new MemoryStream(apk, false), ZipArchiveMode.Read);
        } catch {
            return null;
        }

        using (zip) {
            var arm64 = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase)
                && e.FullName.Contains("arm64-v8a", StringComparison.OrdinalIgnoreCase));
            var chosen = arm64 ?? zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase));
            if (chosen is null) return null;
            using var es = chosen.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            return buf.ToArray();
        }
    }
}

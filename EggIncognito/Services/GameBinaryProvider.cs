using System.Security.Cryptography;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;


public sealed class GameBinaryProvider(
    IServiceProvider services, IConfiguration config, Devices.IDeviceConnectionFactory connections,
    ILogger<GameBinaryProvider> logger) {
    private const string BundleId = "com.auxbrain.egginc";
    private static readonly Lock LiveGate = new();
    private static (string Sha, byte[] Bytes, IReadOnlyList<MachoSymbols.Symbol> Syms, bool Grafted, DateTimeOffset Pulled)? _liveCache;

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;

    public async Task<(bool Ok, byte[]? Bytes, string? Diagnostics)> GetBinaryAsync(string? deviceId, CancellationToken ct) {
        var overridePath = config["Decomp:BinaryPath"];
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
            var bytes = await File.ReadAllBytesAsync(overridePath, ct);
            return (true, bytes, null);
        }

        string? version = await DeviceVersionAsync(deviceId, ct);

        var dir = config["Decomp:SymbolizedIpaDir"];
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine("captures", "ipas");
        var store = new SymbolizedBinaryStore(dir);
        var r = store.Get(version);
        if (!r.Ok || r.Bytes is null) return (false, null, r.Diagnostics);

        if (!r.ExactVersion)
            logger.LogInformation("decomp: device version {Dev} not in stash, using symbolized {Use}", version ?? "?", r.Version);
        return (true, r.Bytes, r.ExactVersion ? null : $"version mismatch: device {version ?? "?"}, using symbolized {r.Version}");
    }

    public async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, bool Grafted, string? Diagnostics)>
        GetLiveBinaryAsync(CancellationToken ct) {
        if (!config.GetValue("Decomp:LiveDevicePull", false))
            return (false, null, null, false, "live device pull disabled (set Decomp:LiveDevicePull=true)");

        if (connections.Ios() is not { } conn)
            return (false, null, null, false, "ios ssh not configured (DeviceCapture:Ios:SshHost + SshKeyPath)");

        int ttlSeconds = config.GetValue("Decomp:LiveCacheSeconds", 900);
        lock (LiveGate) {
            if (_liveCache is { } c && DateTimeOffset.UtcNow - c.Pulled < TimeSpan.FromSeconds(ttlSeconds))
                return (true, c.Bytes, c.Syms, c.Grafted, $"cached live pull (sha {c.Sha[..12]})");
        }

        var puller = new IosBinaryPuller(conn);
        byte[]? pulled;
        try { pulled = await puller.PullBinaryAsync(BundleId, ct); } catch (Exception ex) { return (false, null, null, false, "pull failed: " + ex.Message); }
        if (pulled is null || pulled.Length < 1024) return (false, null, null, false, "pull returned no binary");

        var sha = Convert.ToHexStringLower(SHA256.HashData(pulled));

        var syms = MachoSymbols.Read(pulled);
        bool grafted = false;
        string note = $"live pull sha {sha[..12]}, {syms.Count} native symbols";

        if (syms.Count < 50_000) {
            var dir = config["Decomp:SymbolizedIpaDir"];
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine("captures", "ipas");
            var refr = new SymbolizedBinaryStore(dir).Get(null);
            if (refr.Ok && refr.Bytes is not null) {
                var report = SymbolRecovery.Recover(refr.Bytes, pulled, []);
                if (report.Symbols.Count > syms.Count) {
                    syms = report.Symbols;
                    grafted = true;
                    note = $"live pull sha {sha[..12]} stripped; grafted {report.Recovered} symbols from {refr.Version} ({report.Tier})";
                }
            }
        }

        lock (LiveGate) { _liveCache = (sha, pulled, syms, grafted, DateTimeOffset.UtcNow); }
        return (true, pulled, syms, grafted, note);
    }



    public async Task<(bool Ok, byte[]? RefBytes, byte[]? TargetBytes, string? Diagnostics)> GetRecoveryInputsAsync(
        string? refVersion, string? targetPathOverride, CancellationToken ct) {
        var dir = config["Decomp:SymbolizedIpaDir"];
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine("captures", "ipas");
        var store = new SymbolizedBinaryStore(dir);
        var refr = store.Get(refVersion);
        if (!refr.Ok || refr.Bytes is null) return (false, null, null, refr.Diagnostics);

        var targetPath = targetPathOverride ?? config["Decomp:StrippedTargetPath"];
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            return (false, refr.Bytes, null, "no stripped target binary; set Decomp:StrippedTargetPath or pass targetPath");

        var targetBytes = await File.ReadAllBytesAsync(targetPath, ct);
        return (true, refr.Bytes, targetBytes, null);
    }

    private async Task<string?> DeviceVersionAsync(string? deviceId, CancellationToken ct) {
        var store = Store;
        if (store is null) return null;
        try {
            var latest = await store.LatestPerDeviceAsync(ct);
            DeviceProbe? row = deviceId is null
                ? latest.FirstOrDefault(p => !string.IsNullOrEmpty(p.InstalledAppVersion))
                : latest.FirstOrDefault(p => p.DeviceId == deviceId);
            return row?.InstalledAppVersion;
        } catch { return null; }
    }
}

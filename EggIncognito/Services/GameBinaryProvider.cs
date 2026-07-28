using System.Security.Cryptography;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

public sealed class GameBinaryProvider(
    IServiceProvider services,
    IConfiguration config,
    IDeviceConnectionFactory connections,
    ILogger<GameBinaryProvider> logger) {
    private const string BundleId = "com.auxbrain.egginc";
    private const string Platform = "ios";
    private static readonly Lock LiveGate = new();

    private static (string Sha, byte[] Bytes, IReadOnlyList<MachoSymbols.Symbol> Syms, bool Grafted, DateTimeOffset
        Pulled)? _liveCache;

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private GameBinaryStore? BinaryStore => services.GetService(typeof(GameBinaryStore)) as GameBinaryStore;

    public async Task<(bool Ok, byte[]? Bytes, string? Diagnostics)> GetBinaryAsync(string? deviceId,
        CancellationToken ct) {
        (bool ok, byte[]? bytes, _, string? diag) = await GetBinaryWithVersionAsync(deviceId, ct);
        return (ok, bytes, diag);
    }

    public async Task<(bool Ok, byte[]? Bytes, string Version, string? Diagnostics)> GetBinaryWithVersionAsync(
        string? deviceId, CancellationToken ct) {
        string? version = await DeviceVersionAsync(deviceId, ct);

        string? overridePath = config["Decomp:BinaryPath"];
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
            byte[] bytes = await File.ReadAllBytesAsync(overridePath, ct);
            return (true, bytes, version ?? "unknown", null);
        }

        string? dir = config["Decomp:SymbolizedIpaDir"];
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine("captures", "ipas");
        var store = new SymbolizedBinaryStore(dir);
        var r = store.Get(version);
        if (!r.Ok || r.Bytes is null) return (false, null, "", r.Diagnostics);

        if (!r.ExactVersion) {
            logger.LogInformation("decomp: device version {Dev} not in stash, using symbolized {Use}", version ?? "?",
                r.Version);
        }

        return (true, r.Bytes, r.Version,
            r.ExactVersion ? null : $"version mismatch: device {version ?? "?"}, using symbolized {r.Version}");
    }

    public async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
            Diagnostics)>
        GetExtractionBinaryAsync(CancellationToken ct) {
        string? overridePath = config["Decomp:BinaryPath"];
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
            byte[] ob = await File.ReadAllBytesAsync(overridePath, ct);
            string? ov = await DeviceVersionAsync(null, ct);
            return (true, ob, null, ov ?? "override", $"override binary {overridePath}");
        }

        var dev = await EnsureDeviceBinaryAsync(ct);
        if (dev.Ok && dev.Bytes is not null)
            return (true, dev.Bytes, dev.Symbols, dev.Version, dev.Diagnostics);

        (bool sok, byte[]? sbytes, string sver, string? sdiag) = await GetBinaryWithVersionAsync(null, ct);
        if (sok && sbytes is not null)
            return (true, sbytes, null, sver, $"stale stash fallback ({sver}); {dev.Diagnostics}");

        return (false, null, null, "", $"{dev.Diagnostics}; stash: {sdiag}");
    }

    public async Task<(string Status, string? Version, string? Note)> EnsureCurrentVersionStoredAsync(
        CancellationToken ct) {
        string? version = await DeviceVersionAsync(null, ct);
        if (string.IsNullOrEmpty(version)) return ("no-version", null, "no probe with an installed app version");

        var store = BinaryStore;
        if (store is null) return ("no-store", version, "binary store unavailable");

        try {
            if (await store.ExistsAsync(Platform, version, ct)) return ("stored", version, null);
        } catch (Exception ex) {
            return ("store-error", version, ex.Message);
        }

        if (!config.GetValue("Decomp:LiveDevicePull", false))
            return ("pull-disabled", version, "missing from store; Decomp:LiveDevicePull=false");

        (bool ok, _, _, _, string? diag) = await GetLiveBinaryAsync(ct);
        return ok ? ("pulled", version, diag) : ("pull-failed", version, diag);
    }

    private async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
        Diagnostics)> EnsureDeviceBinaryAsync(CancellationToken ct) {
        string? version = await DeviceVersionAsync(null, ct);
        if (string.IsNullOrEmpty(version))
            return (false, null, null, "", "no device version known (no probe with an installed app version)");

        var store = BinaryStore;
        if (store is not null) {
            StoredBinary? row = null;
            try {
                row = await store.GetAsync(Platform, version, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "stored binary lookup failed for {Version}", version);
            }

            if (row is not null) {
                var resolved = ResolveSymbols(row.Bytes);
                string shaShort = row.Sha256.Length >= 12 ? row.Sha256[..12] : row.Sha256;
                return (true, row.Bytes, resolved.Syms, version, $"stored binary {version} (sha {shaShort}); {resolved.Note}");
            }
        }

        (bool ok, byte[]? bytes, var pulledSyms, _, string? diag) = await GetLiveBinaryAsync(ct);
        if (!ok || bytes is null)
            return (false, null, null, version, $"no stored binary for {version} and live pull failed: {diag}");

        return (true, bytes, pulledSyms, version, $"force-pulled {version}; {diag}");
    }

    public async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, bool Grafted, string?
            Diagnostics)>
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
        try {
            pulled = await puller.PullBinaryAsync(BundleId, ct);
        } catch (Exception ex) {
            return (false, null, null, false, "pull failed: " + ex.Message);
        }

        if (pulled is null || pulled.Length < 1024) return (false, null, null, false, "pull returned no binary");

        string sha = Convert.ToHexStringLower(SHA256.HashData(pulled));
        var resolved = ResolveSymbols(pulled);
        string note = $"live pull sha {sha[..12]}; {resolved.Note}";

        await PersistPullAsync(pulled, sha, resolved.NativeCount, ct);

        lock (LiveGate) _liveCache = (sha, pulled, resolved.Syms, resolved.Grafted, DateTimeOffset.UtcNow);
        return (true, pulled, resolved.Syms, resolved.Grafted, note);
    }

    private (IReadOnlyList<MachoSymbols.Symbol> Syms, bool Grafted, int NativeCount, string Note) ResolveSymbols(
        byte[] bytes) {
        var syms = MachoSymbols.Read(bytes);
        int nativeCount = syms.Count;
        if (nativeCount >= 50_000) return (syms, false, nativeCount, $"{nativeCount} native symbols");

        string? dir = config["Decomp:SymbolizedIpaDir"];
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine("captures", "ipas");
        var refr = new SymbolizedBinaryStore(dir).Get(null);
        if (refr.Ok && refr.Bytes is not null) {
            var report = SymbolRecovery.Recover(refr.Bytes, bytes, []);
            if (report.Symbols.Count > nativeCount) {
                return (report.Symbols, true, nativeCount,
                    $"stripped; grafted {report.Recovered} symbols from {refr.Version} ({report.Tier})");
            }
        }

        return (syms, false, nativeCount, $"{nativeCount} native symbols (no graft reference)");
    }

    private async Task PersistPullAsync(byte[] bytes, string sha, int nativeCount, CancellationToken ct) {
        var store = BinaryStore;
        if (store is null) return;
        string? version = await DeviceVersionAsync(null, ct);
        if (string.IsNullOrEmpty(version)) return;
        try {
            await store.PutAsync(Platform, version, sha, bytes, nativeCount, "live", ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "failed to persist pulled binary {Version}", version);
        }
    }


    public async Task<(bool Ok, byte[]? RefBytes, byte[]? TargetBytes, string? Diagnostics)> GetRecoveryInputsAsync(
        string? refVersion, string? targetPathOverride, CancellationToken ct) {
        string? dir = config["Decomp:SymbolizedIpaDir"];
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine("captures", "ipas");
        var store = new SymbolizedBinaryStore(dir);
        var refr = store.Get(refVersion);
        if (!refr.Ok || refr.Bytes is null) return (false, null, null, refr.Diagnostics);

        string? targetPath = targetPathOverride ?? config["Decomp:StrippedTargetPath"];
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath)) {
            return (false, refr.Bytes, null,
                "no stripped target binary; set Decomp:StrippedTargetPath or pass targetPath");
        }

        byte[] targetBytes = await File.ReadAllBytesAsync(targetPath, ct);
        return (true, refr.Bytes, targetBytes, null);
    }

    private async Task<string?> DeviceVersionAsync(string? deviceId, CancellationToken ct) {
        var store = Store;
        if (store is null) return null;
        try {
            var latest = await store.LatestPerDeviceAsync(ct);
            var row = deviceId is null
                ? latest.FirstOrDefault(p => !string.IsNullOrEmpty(p.InstalledAppVersion))
                : latest.FirstOrDefault(p => p.DeviceId == deviceId);
            return row?.InstalledAppVersion;
        } catch {
            return null;
        }
    }
}

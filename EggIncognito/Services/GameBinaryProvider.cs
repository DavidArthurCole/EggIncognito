using System.Security.Cryptography;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

public sealed class GameBinaryProvider(
    IServiceProvider services,
    IConfiguration config,
    ILogger<GameBinaryProvider> logger) {
    private const string DefaultPlatform = "ios";
    private static readonly Lock LiveGate = new();

#pragma warning disable IDE0028
    private static readonly Dictionary<string, (string Sha, byte[] Bytes, IReadOnlyList<MachoSymbols.Symbol> Syms, bool
        Grafted, DateTimeOffset Pulled)> LiveCache = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private GameBinaryStore? BinaryStore => services.GetService(typeof(GameBinaryStore)) as GameBinaryStore;
    private IDevicePlatforms? Platforms => services.GetService(typeof(IDevicePlatforms)) as IDevicePlatforms;

    public async Task<(bool Ok, byte[]? Bytes, string? Diagnostics)> GetBinaryAsync(string? deviceId,
        CancellationToken ct) {
        (bool ok, byte[]? bytes, _, string? diag) = await GetBinaryWithVersionAsync(deviceId, ct);
        return (ok, bytes, diag);
    }

    public async Task<(bool Ok, byte[]? Bytes, string Version, string? Diagnostics)> GetBinaryWithVersionAsync(
        string? deviceId, CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(deviceId, DefaultPlatform, ct)).Version;

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

    public Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
        Diagnostics)> GetExtractionBinaryAsync(CancellationToken ct) => GetExtractionBinaryAsync(DefaultPlatform, ct);

    public async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
            Diagnostics)>
        GetExtractionBinaryAsync(string platform, CancellationToken ct) {
        bool isDefault = string.Equals(platform, DefaultPlatform, StringComparison.OrdinalIgnoreCase);

        if (isDefault) {
            string? overridePath = config["Decomp:BinaryPath"];
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
                byte[] ob = await File.ReadAllBytesAsync(overridePath, ct);
                string? ov = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
                return (true, ob, null, ov ?? "override", $"override binary {overridePath}");
            }
        }

        var dev = await EnsureDeviceBinaryAsync(platform, ct);
        if (dev.Ok && dev.Bytes is not null)
            return (true, dev.Bytes, dev.Symbols, dev.Version, dev.Diagnostics);

        if (isDefault) {
            (bool sok, byte[]? sbytes, string sver, string? sdiag) = await GetBinaryWithVersionAsync(null, ct);
            if (sok && sbytes is not null)
                return (true, sbytes, null, sver, $"stale stash fallback ({sver}); {dev.Diagnostics}");
            return (false, null, null, "", $"{dev.Diagnostics}; stash: {sdiag}");
        }

        return (false, null, null, dev.Version, dev.Diagnostics);
    }

    public IReadOnlyList<string> ExtractablePlatformsFallback => [DefaultPlatform];

    public async Task<IReadOnlyList<(string Platform, string Status, string? Version, string? Note)>>
        EnsureAllVersionsStoredAsync(CancellationToken ct) {
        var results = new List<(string, string, string?, string?)>();
        var store = Store;
        if (store is null) {
            results.Add((DefaultPlatform, "no-store", null, "device status store unavailable"));
            return results;
        }

        List<Device> devices;
        try {
            devices = await store.EnabledDevicesAsync(ct);
        } catch (Exception ex) {
            results.Add((DefaultPlatform, "store-error", null, ex.Message));
            return results;
        }

        var platforms = devices.Select(d => d.Platform).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (platforms.Count == 0) {
            results.Add((DefaultPlatform, "no-device", null, "no enabled devices"));
            return results;
        }

        foreach (string platform in platforms) {
            (string status, string? version, string? note) = await EnsureCurrentVersionStoredAsync(platform, ct);
            results.Add((platform, status, version, note));
        }

        return results;
    }

    public Task<(string Status, string? Version, string? Note)> EnsureCurrentVersionStoredAsync(CancellationToken ct) =>
        EnsureCurrentVersionStoredAsync(DefaultPlatform, ct);

    public async Task<(string Status, string? Version, string? Note)> EnsureCurrentVersionStoredAsync(string platform,
        CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(version)) return ("no-version", null, "no probe with an installed app version");

        var store = BinaryStore;
        if (store is null) return ("no-store", version, "binary store unavailable");

        try {
            if (await store.ExistsAsync(platform, version, ct)) return ("stored", version, null);
        } catch (Exception ex) {
            return ("store-error", version, ex.Message);
        }

        if (!config.GetValue("Decomp:LiveDevicePull", false))
            return ("pull-disabled", version, "missing from store; Decomp:LiveDevicePull=false");

        (bool ok, _, _, _, string? diag) = await GetLiveBinaryAsync(platform, ct);
        return ok ? ("pulled", version, diag) : ("pull-failed", version, diag);
    }

    private async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
        Diagnostics)> EnsureDeviceBinaryAsync(string platform, CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(version))
            return (false, null, null, "", "no device version known (no probe with an installed app version)");

        var store = BinaryStore;
        if (store is not null) {
            StoredBinary? row = null;
            try {
                row = await store.GetAsync(platform, version, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "stored binary lookup failed for {Platform} {Version}", platform, version);
            }

            if (row is not null) {
                var resolved = ResolveSymbols(row.Bytes);
                string shaShort = row.Sha256.Length >= 12 ? row.Sha256[..12] : row.Sha256;
                return (true, row.Bytes, resolved.Syms, version,
                    $"stored binary {platform} {version} (sha {shaShort}); {resolved.Note}");
            }
        }

        (bool ok, byte[]? bytes, var pulledSyms, _, string? diag) = await GetLiveBinaryAsync(platform, ct);
        if (!ok || bytes is null)
            return (false, null, null, version, $"no stored binary for {platform} {version} and live pull failed: {diag}");

        return (true, bytes, pulledSyms, version, $"force-pulled {platform} {version}; {diag}");
    }

    public Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, bool Grafted, string?
        Diagnostics)> GetLiveBinaryAsync(CancellationToken ct) => GetLiveBinaryAsync(DefaultPlatform, ct);

    public async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, bool Grafted, string?
            Diagnostics)>
        GetLiveBinaryAsync(string platform, CancellationToken ct) {
        if (!config.GetValue("Decomp:LiveDevicePull", false))
            return (false, null, null, false, "live device pull disabled (set Decomp:LiveDevicePull=true)");

        var platforms = Platforms;
        if (platforms is null)
            return (false, null, null, false, "device platform registry unavailable");

        (string? version, Device? device) = await ResolveVersionAndDeviceAsync(null, platform, ct);
        if (device is null)
            return (false, null, null, false, $"no enabled {platform} device");

        int ttlSeconds = config.GetValue("Decomp:LiveCacheSeconds", 900);
        lock (LiveGate) {
            if (LiveCache.TryGetValue(platform, out var c) &&
                DateTimeOffset.UtcNow - c.Pulled < TimeSpan.FromSeconds(ttlSeconds)) {
                return (true, c.Bytes, c.Syms, c.Grafted, $"cached live pull (sha {c.Sha[..12]})");
            }
        }

        var handler = platforms.For(platform);
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);
        DeviceResult<byte[]> pull;
        try {
            pull = await handler.PullAppBinaryAsync(target, ct);
        } catch (Exception ex) {
            return (false, null, null, false, "pull failed: " + ex.Message);
        }

        if (!pull.Ok || pull.Value is null)
            return (false, null, null, false, $"pull {pull.Outcome}: {pull.Note}");

        byte[] pulled = pull.Value;
        if (pulled.Length < 1024) {
            return (false, null, null, false, "pull returned no binary");
        }

        string sha = Convert.ToHexStringLower(SHA256.HashData(pulled));
        var resolved = ResolveSymbols(pulled);
        string note = $"live pull sha {sha[..12]}; {resolved.Note}";

        await PersistPullAsync(platform, version, pulled, sha, resolved.NativeCount, ct);

        lock (LiveGate) {
            LiveCache[platform] = (sha, pulled, resolved.Syms, resolved.Grafted, DateTimeOffset.UtcNow);
        }

        return (true, pulled, resolved.Syms, resolved.Grafted, note);
    }

    private static bool IsElf(byte[] b) =>
        b.Length >= 4 && b[0] == 0x7f && b[1] == 0x45 && b[2] == 0x4c && b[3] == 0x46;

    private (IReadOnlyList<MachoSymbols.Symbol> Syms, bool Grafted, int NativeCount, string Note) ResolveSymbols(
        byte[] bytes) {
        var img = BinaryImage.Load(bytes);
        var syms = img?.Symbols ?? MachoSymbols.Read(bytes);
        int nativeCount = syms.Count;
        if (nativeCount >= 50_000) return (syms, false, nativeCount, $"{nativeCount} native symbols");

        if (IsElf(bytes)) {
            return (syms, false, nativeCount,
                $"{nativeCount} native ELF symbols (Mach-O stash graft not applicable)");
        }

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

    private async Task PersistPullAsync(string platform, string? version, byte[] bytes, string sha, int nativeCount,
        CancellationToken ct) {
        var store = BinaryStore;
        if (store is null) return;
        if (string.IsNullOrEmpty(version)) return;
        try {
            await store.PutAsync(platform, version, sha, bytes, nativeCount, "live", ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "failed to persist pulled binary {Platform} {Version}", platform, version);
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

    private async Task<(string? Version, Device? Device)> ResolveVersionAndDeviceAsync(string? deviceId,
        string platform, CancellationToken ct) {
        var store = Store;
        if (store is null) return (null, null);
        try {
            var devices = await store.EnabledDevicesAsync(ct);
            var device = deviceId is null
                ? devices.FirstOrDefault(d => string.Equals(d.Platform, platform, StringComparison.OrdinalIgnoreCase))
                : devices.FirstOrDefault(d => d.Id == deviceId);
            if (device is null) {
                if (deviceId is null) return (null, null);
                var latestForId = await store.LatestPerDeviceAsync(ct);
                return (latestForId.FirstOrDefault(p => p.DeviceId == deviceId)?.InstalledAppVersion, null);
            }

            var latest = await store.LatestPerDeviceAsync(ct);
            string? version = latest.FirstOrDefault(p => p.DeviceId == device.Id)?.InstalledAppVersion;
            return (version, device);
        } catch {
            return (null, null);
        }
    }
}

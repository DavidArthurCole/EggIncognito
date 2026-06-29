using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

// Supplies a SYMBOLIZED egginc Mach-O to the decomp extractor, version-matched to what the device runs. The
// device build is STRIPPED, so the binary is sourced from the local symbolized-IPA store (mirror/older builds
// carry the ~450k symbols), NOT pulled off the device. The device is only the version oracle. A configured
// Decomp:BinaryPath short-circuits to one explicit symbolized binary. No game binary ever lands in the repo.
public sealed class GameBinaryProvider(
    IServiceProvider services, IConfiguration config, ILogger<GameBinaryProvider> logger)
{
    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;

    public async Task<(bool Ok, byte[]? Bytes, string? Diagnostics)> GetBinaryAsync(string? deviceId, CancellationToken ct)
    {
        var overridePath = config["Decomp:BinaryPath"];
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
        {
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

    // Inputs for v2 symbol recovery: a SYMBOLIZED reference (from the store) + a STRIPPED target. The target is
    // an explicit Decomp:StrippedTargetPath (e.g. the decrypted device binary saved to disk); when unset the
    // recovery has nothing to project onto and returns a diagnostic. Reference defaults to the store's newest
    // symbolized build unless refVersion is given.
    public async Task<(bool Ok, byte[]? RefBytes, byte[]? TargetBytes, string? Diagnostics)> GetRecoveryInputsAsync(
        string? refVersion, string? targetPathOverride, CancellationToken ct)
    {
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

    private async Task<string?> DeviceVersionAsync(string? deviceId, CancellationToken ct)
    {
        var store = Store;
        if (store is null) return null;
        try
        {
            var latest = await store.LatestPerDeviceAsync(ct);
            DeviceProbe? row = deviceId is null
                ? latest.FirstOrDefault(p => !string.IsNullOrEmpty(p.InstalledAppVersion))
                : latest.FirstOrDefault(p => p.DeviceId == deviceId);
            return row?.InstalledAppVersion;
        }
        catch { return null; }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill.Sources;
using EggIncognito.Services.ProtoExtract;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Services.Backfill;

// Thrown when the apk-extract path is asked for on a host without the toolchain configured. The
// controller maps it to 501 "extraction not configured on this host". The list + elgranjero + itunes
// paths work everywhere; APK-extract is opt-in infra (a toolchain host, alongside the farm).
public sealed class ExtractNotConfiguredException(string message) : Exception(message);

// Config the extract path binds. Bound from the ProtoExtract section; Enabled false disables the path.
// Kept a record so config binding is unit-testable in isolation.
public sealed record ProtoExtractOptions
{
    public bool Enabled { get; init; }

    // The path pulls an APK from the network, so it is opt-in per host. Enabled is the whole gate now
    // that the carve is in-process C# (no python/repo to configure).
    public bool IsConfigured => Enabled;
}

// On-demand per-APK proto extraction (the heavy path). Downloads the version's APK from APKPure,
// carves the .proto + the real versionCode in-process (pure C#), then upserts a real (platform, build)
// registry row source "apkpure" with proto. Config-gated: disabled hosts throw
// ExtractNotConfiguredException. The real extract is integration-only (needs a network download);
// unit tests cover only the gate + config binding.
public sealed class ApkExtractService(
    IConfiguration config, ApkPureSource apkPure, IServiceScopeFactory scopeFactory,
    ILogger<ApkExtractService> logger)
{
    public ProtoExtractOptions Options => Bind(config);

    // Binds the ProtoExtract section. Static so a test can bind a canned config without the service.
    public static ProtoExtractOptions Bind(IConfiguration config)
    {
        var s = config.GetSection("ProtoExtract");
        return new ProtoExtractOptions { Enabled = s.GetValue("Enabled", false) };
    }

    public async Task ExtractAsync(string appVersion, CancellationToken ct = default)
    {
        var opts = Options;
        if (!opts.IsConfigured)
            throw new ExtractNotConfiguredException("proto extraction not configured on this host");
        if (string.IsNullOrWhiteSpace(appVersion))
            throw new ArgumentException("appVersion required", nameof(appVersion));

        // The proto lives in the ARM split's lib/arm64-v8a/libegginc.so, NOT base.apk. APKPure serves an
        // XAPK (a zip-of-apks); DownloadArmSplitAsync unzips it and returns the arm64_v8a split bytes, or
        // null when the download is a single base APK with no arm split. The device-pull path (farm)
        // feeds the same arm split directly via ExtractFromArmSplitAsync.
        var armSplit = await apkPure.DownloadArmSplitAsync(appVersion, ct)
            ?? throw new InvalidOperationException($"arm-split download failed for {appVersion}");

        await ExtractFromArmSplitAsync(armSplit, appVersion, "apkpure", ct);
    }

    // Shared pipeline tail over the arm split bytes: carve the proto + real versionCode in-process,
    // upsert the registry row. Both the APKPure XAPK-unzip path and the device-farm pull feed this
    // with their own source label. Gated like ExtractAsync; callable directly for the farm.
    public async Task ExtractFromArmSplitAsync(
        byte[] armSplitBytes, string appVersion, string source, CancellationToken ct = default)
    {
        var opts = Options;
        if (!opts.IsConfigured)
            throw new ExtractNotConfiguredException("proto extraction not configured on this host");
        if (armSplitBytes is null || armSplitBytes.Length == 0)
            throw new ArgumentException("arm split bytes required", nameof(armSplitBytes));
        if (string.IsNullOrWhiteSpace(appVersion))
            throw new ArgumentException("appVersion required", nameof(appVersion));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("source required", nameof(source));

        var proto = AndroidProtoExtractor.ExtractProtoText(armSplitBytes);

        // versionCode parsed from the binary AndroidManifest; null means we could not learn the real
        // build. Do NOT forge build = appVersion (that would mint a fake (platform, build) key).
        var build = ApkVersionCode.Read(armSplitBytes);

        using var scope = scopeFactory.CreateScope();

        if (build is null)
        {
            // Unknown build: record the sighting in known_versions so the discovery list reflects it,
            // and skip the build-keyed registry write rather than minting a fabricated key. Promote to
            // a real registry row once the build is learned (a later farm/device extract).
            var jobs = scope.ServiceProvider.GetService<IBackfillJobStore>();
            if (jobs is not null)
                await jobs.UpsertKnownAsync("android", appVersion, null, null, source, ct);
            logger.LogWarning(
                "backfill: apk-extract {Version} produced a proto but versionCode was unreadable; " +
                "skipped registry write (no fabricated build key)", appVersion);
            return;
        }

        var store = scope.ServiceProvider.GetService<IProtoBackfillStore>()
            ?? throw new InvalidOperationException("no store (no DB)");
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proto))).ToLowerInvariant();
        var index = JsonSerializer.Serialize(EggIncognito.Services.ProtoTextIndex.Names(proto));
        await store.BackfillUpsertAsync("android", appVersion, build, null, "com.auxbrain.egginc",
            proto, sha, index, writeProto: true, $"{source}:{appVersion}", DateTimeOffset.UtcNow, source, ct);
        logger.LogInformation("backfill: apk-extract {Version} build {Build} done ({Source})", appVersion, build, source);
    }
}

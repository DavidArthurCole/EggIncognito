using System.Diagnostics;
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

// Config the extract path binds. Bound from the ProtoExtract section; Enabled false (or a missing
// python/repo path) disables the path. Kept a record so config binding is unit-testable in isolation.
public sealed record ProtoExtractOptions
{
    public bool Enabled { get; init; }
    public string? PythonPath { get; init; }
    public string? RepoPath { get; init; }

    // The path is usable only when explicitly enabled AND both tool paths are set. A half-configured
    // host (enabled but no python) is treated as not configured, not a half-broken run.
    public bool IsConfigured => Enabled
        && !string.IsNullOrWhiteSpace(PythonPath)
        && !string.IsNullOrWhiteSpace(RepoPath);
}

// On-demand per-APK proto extraction (the heavy path). Downloads the version's APK from APKPure, runs
// the pbtk toolchain (the same one the farm uses) to recover the .proto text + the real versionCode,
// then upserts a real (platform, build) registry row source "apkpure" with proto. Config-gated:
// disabled hosts throw ExtractNotConfiguredException. The real extract is integration-only (needs the
// toolchain); unit tests cover only the gate + config binding.
public sealed class ApkExtractService(
    IConfiguration config, ApkPureSource apkPure, IServiceScopeFactory scopeFactory,
    ILogger<ApkExtractService> logger)
{
    public ProtoExtractOptions Options => Bind(config);

    // Binds the ProtoExtract section. Static so a test can bind a canned config without the service.
    public static ProtoExtractOptions Bind(IConfiguration config)
    {
        var s = config.GetSection("ProtoExtract");
        return new ProtoExtractOptions
        {
            Enabled = s.GetValue("Enabled", false),
            PythonPath = s["PythonPath"],
            RepoPath = s["RepoPath"],
        };
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

    // Shared pipeline tail over the arm split bytes: write temp, run the toolchain, read the real
    // versionCode, upsert the registry row. Both the APKPure XAPK-unzip path and the device-farm pull
    // feed this with their own source label. Gated like ExtractAsync; callable directly for the farm.
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

        var tmp = Path.Combine(Path.GetTempPath(), $"egi-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var apkPath = Path.Combine(tmp, "app.apk");
        try
        {
            await File.WriteAllBytesAsync(apkPath, armSplitBytes, ct);
            var proto = await RunPbtkAsync(opts, apkPath, ct);

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
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    // Runs the EggIncProtoExtractor toolchain over the ARM split and returns the cleaned ei.proto text.
    // jar_extract.py decompiles the native lib into an out dir as ei.proto + common.proto; the cleanup
    // step (merge common into ei after `package ei;`, drop the import, strip aux. prefixes) is now the
    // C# ProtoCleanup rather than the python protocleanup.py: one fewer subprocess and the parity is
    // unit-testable. Produces the bytes whose sha256 is the canonical protoSha. Integration-only.
    private static async Task<string> RunPbtkAsync(ProtoExtractOptions opts, string apkPath, CancellationToken ct)
    {
        var outDir = Path.Combine(Path.GetTempPath(), $"egi-pbtk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            await RunPythonAsync(opts, ct, Path.Combine("pbtk", "extractors", "jar_extract.py"), apkPath, outDir);
            var eiPath = Path.Combine(outDir, "ei.proto");
            var commonPath = Path.Combine(outDir, "common.proto");
            if (!File.Exists(eiPath))
                throw new InvalidOperationException($"pbtk produced no ei.proto in {outDir}");
            var ei = await File.ReadAllTextAsync(eiPath, ct);
            var common = File.Exists(commonPath) ? await File.ReadAllTextAsync(commonPath, ct) : "";
            return ProtoCleanup.Clean(ei, common);
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    // One python invocation against the extractor repo (its venv interpreter + checkout as cwd), with
    // -W ignore to silence the pyqt deprecation noise the toolchain emits.
    private static async Task RunPythonAsync(
        ProtoExtractOptions opts, CancellationToken ct, string script, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = opts.PythonPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = opts.RepoPath!,
        };
        psi.ArgumentList.Add("-W");
        psi.ArgumentList.Add("ignore");
        psi.ArgumentList.Add(script);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start python");
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{script} exit {proc.ExitCode}: {stderr.Trim()}\n{stdout.Trim()}");
    }
}

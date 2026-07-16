using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill.Sources;
using EggIncognito.Services.ProtoExtract;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Services.Backfill;


public sealed class ExtractNotConfiguredException(string message) : Exception(message);

public sealed record ProtoExtractOptions
{
    public bool Enabled { get; init; }

   
   
    public bool IsConfigured => Enabled;
}


public sealed class ApkExtractService(
    IConfiguration config, ApkPureSource apkPure, IServiceScopeFactory scopeFactory,
    ILogger<ApkExtractService> logger)
{
    public ProtoExtractOptions Options => Bind(config);

   
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

       
       
       
        var armSplit = await apkPure.DownloadArmSplitAsync(appVersion, ct)
            ?? throw new InvalidOperationException($"arm-split download failed for {appVersion}");

        await ExtractFromArmSplitAsync(armSplit, appVersion, "apkpure", ct);
    }

   
   
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

       
       
        var build = ApkVersionCode.Read(armSplitBytes);

        using var scope = scopeFactory.CreateScope();

        if (build is null)
        {
           
           
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

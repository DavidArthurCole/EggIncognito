using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EggIncognito.Data.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Services.Backfill;

// Walks elgranjero/EggIncProtos history, upserting one registry row per version commit with its
// ei.proto. Idempotent (keyed on build), precedence-aware (never clobbers farm proto). On-demand and
// admin-triggered: it runs in a background task, so it opens its own DI scope to get a valid scoped
// store + DbContext rather than capturing a request-scoped one that the response would dispose.
public sealed class ElgranjeroImporter(
    IGitHubClient github, IServiceScopeFactory scopeFactory, ILogger<ElgranjeroImporter> logger)
{
    private const string Repo = "elgranjero/EggIncProtos";
    private static readonly string[] ProtoPaths = ["ei.proto", "ei/ei.proto"];

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IProtoBackfillStore>();
        if (store is null)
        {
            logger.LogWarning("backfill: no store available (no DB), elgranjero import skipped");
            return 0;
        }

        var imported = 0;
        await foreach (var commit in github.CommitsAsync(Repo, ct))
        {
            var v = ElgranjeroParse.FromMessage(commit.Message);
            if (v is null) continue;

            var proto = await github.FileAtAsync(Repo, commit.Sha, ProtoPaths, ct);
            var existing = await store.GetAsync("android", v.Build, ct);
            var writeProto = proto is not null
                && (existing is null || SourcePrecedence.MayOverwriteProto(existing.Source, "elgranjero"));
            string? sha = null, index = null;
            if (writeProto)
            {
                sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proto!))).ToLowerInvariant();
                index = JsonSerializer.Serialize(EggIncognito.Services.ProtoTextIndex.Names(proto!));
            }

            await store.BackfillUpsertAsync("android", v.AppVersion, v.Build, v.ClientVersion,
                "com.auxbrain.egginc", proto, sha, index, writeProto,
                $"elgranjero@{commit.Sha[..7]}", commit.Date, "elgranjero", ct);
            imported++;
            logger.LogInformation("backfill: {Build} ({App}) from elgranjero", v.Build, v.AppVersion);
        }
        logger.LogInformation("backfill: elgranjero import done, {N} versions", imported);
        return imported;
    }
}

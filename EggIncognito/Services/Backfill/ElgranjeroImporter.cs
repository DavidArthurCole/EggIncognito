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

    public async Task<int> RunAsync(string? startedBy = null, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IProtoBackfillStore>();
        if (store is null)
        {
            logger.LogWarning("backfill: no store available (no DB), elgranjero import skipped");
            return 0;
        }
        var jobs = scope.ServiceProvider.GetService<IBackfillJobStore>();
        var job = jobs is null ? null : await jobs.StartAsync("elgranjero", startedBy, ct);

        try
        {
            var imported = 0;
            await foreach (var commit in github.CommitsAsync(Repo, ct))
            {
                if (await ImportCommitAsync(store, commit, ct)) imported++;
                else continue;
                if (job is not null && imported % 25 == 0) await jobs!.BumpAsync(job.Id, imported, ct: ct);
            }
            if (job is not null)
            {
                await jobs!.BumpAsync(job.Id, imported, ct: ct);
                await jobs.FinishAsync(job.Id, "done", $"{imported} versions", ct);
            }
            logger.LogInformation("backfill: elgranjero import done, {N} versions", imported);
            return imported;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: elgranjero import failed");
            if (job is not null) await jobs!.FinishAsync(job.Id, "failed", ex.Message, ct);
            throw;
        }
    }

    // Imports one commit's version + proto if it parses; returns whether a row was written.
    private async Task<bool> ImportCommitAsync(IProtoBackfillStore store, GitHubClient.Commit commit, CancellationToken ct)
    {
        var v = ElgranjeroParse.FromMessage(commit.Message);
        if (v is null) return false;

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
        logger.LogInformation("backfill: {Build} ({App}) from elgranjero", v.Build, v.AppVersion);
        return true;
    }
}

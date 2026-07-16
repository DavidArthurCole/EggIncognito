using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Services.Backfill;
public sealed class VersionListImporter(
    IServiceScopeFactory scopeFactory, ILogger<VersionListImporter> logger)
{
    public async Task<int> RunAsync(IVersionListSource source, string? startedBy = null, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetService<IBackfillJobStore>();
        if (jobs is null)
        {
            logger.LogWarning("backfill: no job store (no DB), {Source} list import skipped", source.Name);
            return 0;
        }

        var job = await jobs.StartAsync(source.Name, startedBy, ct);
        try
        {
            var versions = await source.FetchAsync(ct);
            var imported = 0;
            foreach (var v in versions)
            {
                if (string.IsNullOrWhiteSpace(v.AppVersion)) continue;
                await jobs.UpsertKnownAsync(source.Platform, v.AppVersion, v.ReleaseDate, v.Changelog, source.Name, ct);
                imported++;
                if (imported % 25 == 0) await jobs.BumpAsync(job.Id, imported, ct: ct);
            }
            await jobs.BumpAsync(job.Id, imported, ct: ct);
            await jobs.FinishAsync(job.Id, "done", $"{imported} known versions", ct);
            logger.LogInformation("backfill: {Source} list import done, {N} versions", source.Name, imported);
            return imported;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: {Source} list import failed", source.Name);
            await jobs.FinishAsync(job.Id, "failed", ex.Message, ct);
            return 0;
        }
    }
}

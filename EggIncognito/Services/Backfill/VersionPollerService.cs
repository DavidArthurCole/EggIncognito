using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill.Sources;

namespace EggIncognito.Services.Backfill;

public sealed class VersionPollerService(
    IServiceScopeFactory scopeFactory,
    VersionPollerOptions options,
    TimeProvider time,
    ILogger<VersionPollerService> logger) : BackgroundService
{
   
    private static readonly (string Platform, string Source)[] PlatformSources =
        [("android", "fandom"), ("ios", "itunes")];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("version poller disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.PollIntervalMinutes)), time);
        try
        {
            await PollOnceAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PollOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    internal async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(IBackfillJobStore)) is not IBackfillJobStore jobs)
            return;

        var before = (await jobs.KnownAsync(ct))
            .Select(k => (k.Platform, k.AppVersion)).ToHashSet();

        foreach (var (platform, sourceKey) in PlatformSources)
        {
            if (!options.Platforms.Contains(platform)) continue;
            if (sp.GetKeyedService<IVersionListSource>(sourceKey) is not IVersionListSource source) continue;

            IReadOnlyList<ListedVersion> versions;
            try { versions = await source.FetchAsync(ct); }
            catch (Exception ex) { logger.LogWarning(ex, "version poll: {Source} fetch failed", sourceKey); continue; }

            foreach (var v in versions)
            {
                if (string.IsNullOrWhiteSpace(v.AppVersion)) continue;
                await jobs.UpsertKnownAsync(platform, v.AppVersion, v.ReleaseDate, v.Changelog, sourceKey, ct);

                var isNew = !before.Contains((platform, v.AppVersion));
                if (isNew && options.AutoQueueExtract)
                {
                    await jobs.StartExtractAsync(platform, v.AppVersion, ct);
                    logger.LogInformation("version poll: new {Platform} {Version} -> extract queued", platform, v.AppVersion);
                }
            }
        }
    }
}

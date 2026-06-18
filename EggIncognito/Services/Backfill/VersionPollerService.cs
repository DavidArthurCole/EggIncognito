using EggIncognito.Data.Services;
using EggIncognito.Services.Backfill.Sources;

namespace EggIncognito.Services.Backfill;

// Proactive store poller: on a schedule, fetches the iOS App Store (iTunes Lookup API) + Google Play
// (via the APKPure mirror, which exposes Play version numbers) and records newly seen versions in the
// known-versions discovery list. Newly discovered versions optionally queue an extract job (Android runs
// end-to-end via the APK toolchain; iOS records intent until a binary is supplied). DB-gated: a no-DB or
// disabled host no-ops. Modeled on CaptureSweeper's PeriodicTimer loop.
//
// Feed notification is intentionally NOT fired here: the Discord feed dedups on a real proto_version id,
// and discovery has no proto yet. The feed fires when the queued extract produces a ProtoVersion (the
// existing sync path), so subscribers learn of a version once its proto is actually available.
public sealed class VersionPollerService(
    IServiceScopeFactory scopeFactory,
    VersionPollerOptions options,
    TimeProvider time,
    ILogger<VersionPollerService> logger) : BackgroundService
{
    // The poll source per platform. Android = Fandom's Version_History (MediaWiki JSON API): structured,
    // changelog-bearing, and not bot-blocked. APKPure's versions page Cloudflare-403s the scrape client, so
    // it is the APK-DOWNLOAD source only, never the list source. iOS = iTunes App Store lookup.
    private static readonly (string Platform, string Source)[] PlatformSources =
        [("android", "fandom"), ("ios", "itunes")];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("version poller disabled");
            return;
        }

        // First poll shortly after boot, then on the configured interval.
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
            return; // no DB on this host

        // Snapshot known versions up front so we can tell which are genuinely new this tick.
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

using System.Security.Cryptography;
using System.Text;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.DataApi;
using Ei;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Feed;

public sealed class PeriodicalsChangeNotifier(
    IServiceScopeFactory scopes,
    IConfiguration config,
    DataCatalog catalog,
    IRouteCatalog routes,
    ILogger<PeriodicalsChangeNotifier> logger)
    : IEndpointWriteObserver {
    public void OnEndpointWritten(string routePath, string json, string? previousJson = null) {
        if (catalog.ByWireRoute(routePath)?.Feed is not { } feed) return;
        var aspects = ComputeAspects(routePath, previousJson, json);
        if (aspects is null) {
            logger.LogInformation("periodicals change detected on {Route} (feed {Feed})", routePath, feed);
        } else {
            logger.LogInformation(
                "periodicals change detected on {Route} (feed {Feed}): changed [{Aspects}], new events [{Events}], new contracts [{Contracts}], new colleggtibles [{Colleggtibles}]",
                routePath, feed,
                string.Join(", ", aspects.ChangedAspects), string.Join(", ", aspects.AddedEvents),
                string.Join(", ", aspects.AddedContracts), string.Join(", ", aspects.AddedColleggtibles));
        }

        string sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ProtoJson.StripVolatile(json))));
        string pageUrl = PageUrl(config["Feed:PageBaseUrl"]);
        _ = Task.Run(async () => {
            try {
                using var scope = scopes.CreateScope();
                await UpsertStoredEndpointAsync(scope.ServiceProvider, routePath, json);
                await InsertSnapshotAsync(scope.ServiceProvider, routePath, json, sha);
                var dispatcher = scope.ServiceProvider.GetService<FeedDispatcher>();
                if (dispatcher is null) return;
                await dispatcher.DispatchAsync(new PeriodicalsChangedEvent(feed, sha, pageUrl, aspects));
            } catch (Exception ex) {
                logger.LogWarning(ex, "periodicals-change dispatch for {Feed} threw", feed);
            }
        });
    }

    private PeriodicalsAspectSummary? ComputeAspects(string route, string? previousJson, string json) {
        if (previousJson is null) return null;
        if (!string.Equals(routes.Get(route)?.Response, PeriodicalsResponse.Descriptor.Name, StringComparison.Ordinal))
            return null;
        try {
            var prev = JsonParser.Default.Parse<PeriodicalsResponse>(ProtoJson.StripVolatile(previousJson));
            var next = JsonParser.Default.Parse<PeriodicalsResponse>(ProtoJson.StripVolatile(json));
            PeriodicalsSanitizer.ScrubPlayerScope(prev);
            PeriodicalsSanitizer.ScrubPlayerScope(next);
            return ComputeSummary(prev, next);
        } catch (Exception ex) {
            logger.LogDebug(ex, "periodicals aspect diff for {Route} failed", route);
            return null;
        }
    }

    private static PeriodicalsAspectSummary ComputeSummary(PeriodicalsResponse prev, PeriodicalsResponse next) {
        var changed = new List<string>();
        if (!Equals(prev.Sales, next.Sales)) changed.Add("sales");
        if (!Equals(prev.Events, next.Events)) changed.Add("events");

        var prevContracts = prev.Contracts?.Clone();
        var nextContracts = next.Contracts?.Clone();
        prevContracts?.CustomEggs.Clear();
        nextContracts?.CustomEggs.Clear();
        if (!Equals(prevContracts, nextContracts)) changed.Add("contracts");

        var prevEggs = Eggs(prev);
        var nextEggs = Eggs(next);
        if (!prevEggs.SequenceEqual(nextEggs)) changed.Add("colleggtibles");
        if (!Equals(prev.LiveConfig, next.LiveConfig)) changed.Add("liveConfig");
        if (!Equals(prev.MailBag, next.MailBag)) changed.Add("mail");

        return new PeriodicalsAspectSummary(
            changed,
            AddedIds(EventIds(prev), EventIds(next)),
            AddedIds(ContractIds(prev), ContractIds(next)),
            AddedIds(prevEggs.Select(e => e.Identifier), nextEggs.Select(e => e.Identifier)));
    }

    private static Google.Protobuf.Collections.RepeatedField<CustomEgg> Eggs(PeriodicalsResponse r) =>
        r.Contracts?.CustomEggs ?? [];

    private static IEnumerable<string> EventIds(PeriodicalsResponse r) =>
        (r.Events?.Events ?? []).Select(e => e.Identifier);

    private static IEnumerable<string> ContractIds(PeriodicalsResponse r) =>
        (r.Contracts?.Contracts ?? []).Select(c => c.Identifier);

    private static string[] AddedIds(IEnumerable<string> prev, IEnumerable<string> next) =>
        [.. next.Where(id => !string.IsNullOrEmpty(id)).Except(prev, StringComparer.Ordinal)];

    private async Task UpsertStoredEndpointAsync(IServiceProvider sp, string route, string json) {
        try {
            if (sp.GetService<EggIncognitoDbContext>() is not { } db) return;
            string responseType = routes.Get(route)?.Response ?? "";
            var existing = await db.StoredEndpoints
                .FirstOrDefaultAsync(e => e.Path == route && e.Eid == null);
            if (existing is null) {
                db.StoredEndpoints.Add(new StoredEndpoint {
                    Path = route,
                    Eid = null,
                    ResponseJson = json,
                    ResponseType = responseType,
                    OwnerUserId = null
                });
            } else {
                existing.ResponseJson = json;
                existing.ResponseType = responseType;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync();
        } catch (Exception ex) {
            logger.LogWarning(ex, "periodicals-change stored_endpoint upsert for {Route} failed", route);
        }
    }

    private async Task InsertSnapshotAsync(IServiceProvider sp, string route, string json, string sha) {
        try {
            if (!string.Equals(routes.Get(route)?.Response, PeriodicalsResponse.Descriptor.Name, StringComparison.Ordinal))
                return;
            if (sp.GetService<EggIncognitoDbContext>() is not { } db) return;
            if (await db.PeriodicalsSnapshots.AnyAsync(s => s.Sha == sha)) return;
            db.PeriodicalsSnapshots.Add(new PeriodicalsSnapshot {
                CapturedAt = DateTimeOffset.UtcNow,
                Sha = sha,
                ResponseJson = json
            });
            await db.SaveChangesAsync();
        } catch (Exception ex) {
            logger.LogWarning(ex, "periodicals snapshot insert for {Route} failed", route);
        }
    }

    private static string PageUrl(string? baseUrl) =>
        $"{(string.IsNullOrEmpty(baseUrl) ? FeedDispatcher.DefaultPageBaseUrl : baseUrl.TrimEnd('/'))}/periodicals";
}

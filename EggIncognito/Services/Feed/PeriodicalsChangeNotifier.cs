using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Services.Feed;

public sealed class PeriodicalsChangeNotifier(
    IServiceScopeFactory scopes, IConfiguration config, ILogger<PeriodicalsChangeNotifier> logger)
    : IEndpointWriteObserver
{
    private static readonly IReadOnlyDictionary<string, string> FeedByRoute = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ei/get_periodicals"] = "periodicals",
        ["ei_afx/config"] = "afx-config",
        ["ei_ctx/get_season_infos_v2"] = "season-infos",
    };

    public void OnEndpointWritten(string routePath, string json)
    {
        if (!FeedByRoute.TryGetValue(routePath, out var feed)) return;
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var pageUrl = PageUrl(config["Feed:PageBaseUrl"]);
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetService<FeedDispatcher>();
                if (dispatcher is null) return;
                await dispatcher.DispatchAsync(new PeriodicalsChangedEvent(feed, sha, pageUrl));
            }
            catch (Exception ex) { logger.LogWarning(ex, "periodicals-change dispatch for {Feed} threw", feed); }
        });
    }

    private static string PageUrl(string? baseUrl) =>
        $"{(string.IsNullOrEmpty(baseUrl) ? FeedDispatcher.DefaultPageBaseUrl : baseUrl!.TrimEnd('/'))}/periodicals";
}

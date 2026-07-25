using System.Security.Cryptography;
using System.Text;
using EggIncognito.Services.DataApi;

namespace EggIncognito.Services.Feed;

public sealed class PeriodicalsChangeNotifier(
    IServiceScopeFactory scopes,
    IConfiguration config,
    DataCatalog catalog,
    ILogger<PeriodicalsChangeNotifier> logger)
    : IEndpointWriteObserver {
    public void OnEndpointWritten(string routePath, string json) {
        if (catalog.ByWireRoute(routePath)?.Feed is not { } feed) return;
        string sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        string pageUrl = PageUrl(config["Feed:PageBaseUrl"]);
        _ = Task.Run(async () => {
            try {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetService<FeedDispatcher>();
                if (dispatcher is null) return;
                await dispatcher.DispatchAsync(new PeriodicalsChangedEvent(feed, sha, pageUrl));
            } catch (Exception ex) {
                logger.LogWarning(ex, "periodicals-change dispatch for {Feed} threw", feed);
            }
        });
    }

    private static string PageUrl(string? baseUrl) =>
        $"{(string.IsNullOrEmpty(baseUrl) ? FeedDispatcher.DefaultPageBaseUrl : baseUrl.TrimEnd('/'))}/periodicals";
}

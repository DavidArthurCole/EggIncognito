using EggIncognito.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Data.Services;

// Singleton-safe IDbRouteProvider: opens a DI scope per call so it can use the scoped DbRouteProvider,
// which depends on the scoped DbContext. Route lookups are infrequent, so per-call scope cost is
// negligible.
public sealed class ScopedDbRouteProvider(IServiceScopeFactory scopeFactory) : IDbRouteProvider
{
    public RouteInfo? GetDbRoute(string path)
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DbRouteProvider>().GetDbRoute(path);
    }

    public IReadOnlyList<RouteInfo> AllDbRoutes()
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DbRouteProvider>().AllDbRoutes();
    }
}

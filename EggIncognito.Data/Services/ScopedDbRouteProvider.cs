using EggIncognito.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Data.Services;

public sealed class ScopedDbRouteProvider(IServiceScopeFactory scopeFactory) : IDbRouteProvider {
    public RouteInfo? GetDbRoute(string path) {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DbRouteProvider>().GetDbRoute(path);
    }

    public IReadOnlyList<RouteInfo> AllDbRoutes() {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DbRouteProvider>().AllDbRoutes();
    }

    public void Invalidate() {
    }
}

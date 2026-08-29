using EggIncognito.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Data.Services;

public sealed class ScopedBinaryRouteProvider(IServiceScopeFactory scopeFactory) : IBinaryRouteProvider {
    public BinaryRouteInfo? GetBinaryRoute(string path) {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BinaryRouteProvider>().GetBinaryRoute(path);
    }

    public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BinaryRouteProvider>().AllBinaryRoutes();
    }

    public void Invalidate() {
    }
}

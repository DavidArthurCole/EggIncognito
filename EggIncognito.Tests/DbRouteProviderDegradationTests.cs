using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// Regression: a transient Postgres error must degrade to yaml-only routing, not throw out of route
// lookup. The context targets an unreachable host (closed local port) so every query throws fast.
public class DbRouteProviderDegradationTests
{
    private static DbContextOptions<EggIncognitoDbContext> UnreachableOptions()
        => new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
            .Options;

    [Fact]
    public void GetDbRoute_DbError_ReturnsNull()
    {
        using var ctx = new EggIncognitoDbContext(UnreachableOptions());
        var provider = new DbRouteProvider(ctx, NullLogger<DbRouteProvider>.Instance);
        Assert.Null(provider.GetDbRoute("ei/anything"));
    }

    [Fact]
    public void AllDbRoutes_DbError_ReturnsEmpty()
    {
        using var ctx = new EggIncognitoDbContext(UnreachableOptions());
        var provider = new DbRouteProvider(ctx, NullLogger<DbRouteProvider>.Instance);
        Assert.Empty(provider.AllDbRoutes());
    }

    [Fact]
    public void MergedCatalog_DbError_StillServesYaml()
    {
        var yamlPath = Path.Combine(Path.GetTempPath(), $"ei-routes-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(yamlPath, """
routes:
  - path: ei/known
    request: GetPeriodicalsRequest
    response: PeriodicalsResponse
""");
        using var ctx = new EggIncognitoDbContext(UnreachableOptions());
        var provider = new DbRouteProvider(ctx, NullLogger<DbRouteProvider>.Instance);
        var merged = new MergedRouteCatalog(new RouteCatalog(yamlPath), provider);
        Assert.Equal("PeriodicalsResponse", merged.Get("ei/known")!.Response);
        Assert.Single(merged.All());
    }
}

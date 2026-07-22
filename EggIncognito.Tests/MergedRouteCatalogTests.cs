using EggIncognito.Services;

namespace EggIncognito.Tests;

public class MergedRouteCatalogTests {
    private sealed class FakeDb(params RouteInfo[] routes) : IDbRouteProvider {
        private readonly Dictionary<string, RouteInfo> _map = routes.ToDictionary(r => r.Path);
        public RouteInfo? GetDbRoute(string path) => _map.GetValueOrDefault(path);
        public IReadOnlyList<RouteInfo> AllDbRoutes() => routes;
    }

    private static RouteCatalog Yaml(string yaml) {
        var p = Path.Combine(Path.GetTempPath(), $"ei-routes-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(p, yaml);
        return new RouteCatalog(p);
    }

    private const string YamlText = """
routes:
  - path: ei/known
    request: GetPeriodicalsRequest
    response: PeriodicalsResponse
""";

    [Fact]
    public void YamlRoute_WinsOverDb() {
        var dbRoute = new RouteInfo("ei/known", "X", "Y", false, false, null, false, false);
        var merged = new MergedRouteCatalog(Yaml(YamlText), new FakeDb(dbRoute));
        Assert.Equal("PeriodicalsResponse", merged.Get("ei/known")!.Response);
    }

    [Fact]
    public void DbRoute_FillsNewPath() {
        var dbRoute = new RouteInfo("ei/dbonly", null, "PeriodicalsResponse", false, false, null, false, false);
        var merged = new MergedRouteCatalog(Yaml(YamlText), new FakeDb(dbRoute));
        Assert.Equal("PeriodicalsResponse", merged.Get("ei/dbonly")!.Response);
    }

    [Fact]
    public void NullDb_IsYamlOnly() {
        var merged = new MergedRouteCatalog(Yaml(YamlText), null);
        Assert.Null(merged.Get("ei/dbonly"));
        Assert.Single(merged.All());
    }

    [Fact]
    public void All_DedupesYamlAndDb() {
        var merged = new MergedRouteCatalog(Yaml(YamlText),
            new FakeDb(new RouteInfo("ei/dbonly", null, "PeriodicalsResponse", false, false, null, false, false)));
        Assert.Equal(2, merged.All().Count);
    }
}

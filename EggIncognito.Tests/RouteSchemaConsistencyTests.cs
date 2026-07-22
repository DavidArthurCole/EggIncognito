using EggIncognito.RouteGenerator;
using EggIncognito.Services;

namespace EggIncognito.Tests;


public sealed class RouteSchemaConsistencyTests {
    private static string RealYamlPath() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "EggIncognito", "RouteMap", "routes.yaml");
            dir = dir.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "EggIncognito", "RouteMap", "routes.yaml");
    }

    [Fact]
    public void BothParsers_AgreeOnEveryRoute() {
        var yamlPath = RealYamlPath();
        Assert.True(File.Exists(yamlPath), $"routes.yaml not found at {yamlPath}");
        var yaml = File.ReadAllText(yamlPath);

        var gen = RouteParser.Parse(yaml).ToDictionary(e => e.Path);
        var cat = RouteCatalog.Parse(yaml).ToDictionary(e => e.Path);


        Assert.Equal(gen.Keys.OrderBy(k => k), cat.Keys.OrderBy(k => k));

        foreach (var path in gen.Keys) {
            var g = gen[path];
            var c = cat[path];

            Assert.Equal(g.Request, c.Request);
            Assert.Equal(g.Response, c.Response);
            Assert.Equal(g.RequestWrapped, c.RequestWrapped);
            Assert.Equal(g.ResponseWrapped, c.ResponseWrapped);
            Assert.Equal(g.RawResponse, c.RawResponse);
            Assert.Equal(g.PathParam, c.PathParam);
            Assert.Equal(g.PathParamOnly, c.PathParamOnly);
        }
    }

    [Fact]
    public void LegacyAndNewKeyForms_ProduceIdenticalShape_AcrossParsers() {
        const string legacy = """
            routes:
              - path: ei/x
                requestType: AuthenticatedMessage
                responseType: FooResponse
            """;
        const string modern = """
            routes:
              - path: ei/x
                requestWrapped: true
                response: FooResponse
            """;

        var gLegacy = RouteParser.Parse(legacy)[0];
        var gModern = RouteParser.Parse(modern)[0];
        Assert.Equal(gLegacy.Request, gModern.Request);
        Assert.Equal(gLegacy.Response, gModern.Response);
        Assert.Equal(gLegacy.RequestWrapped, gModern.RequestWrapped);

        var cLegacy = RouteCatalog.Parse(legacy)[0];
        var cModern = RouteCatalog.Parse(modern)[0];
        Assert.Equal(cLegacy.Request, cModern.Request);
        Assert.Equal(cLegacy.Response, cModern.Response);
        Assert.Equal(cLegacy.RequestWrapped, cModern.RequestWrapped);
    }

    [Fact]
    public void EntriesOutsideRoutesSection_AreIgnored_ByBothParsers() {
        const string yaml = """
            routes:
              - path: ei/real
                requestType: ConfigRequest
                responseType: ConfigResponse

            excluded:
              - path: ei/bogus
                responseType: ConfigResponse

            endpoint_status:
              empty:
                - ei/real
            """;

        var g = RouteParser.Parse(yaml);
        var c = RouteCatalog.Parse(yaml);

        Assert.Single(g);
        Assert.Equal("ei/real", g[0].Path);
        Assert.Equal("ConfigResponse", g[0].Response);
        Assert.Single(c);
        Assert.Equal("ei/real", c[0].Path);
        Assert.Equal("ConfigResponse", c[0].Response);
    }

    [Fact]
    public void EmptyPathEntry_EmitsNoRoute_AndDoesNotCorruptNeighbors() {
        const string yaml = """
            routes:
              - path: ei/real
                requestType: ConfigRequest
              - path:
                responseType: ConfigResponse
            """;

        var g = RouteParser.Parse(yaml);
        var c = RouteCatalog.Parse(yaml);


        Assert.Single(g);
        Assert.Equal("ei/real", g[0].Path);
        Assert.Null(g[0].Response);
        Assert.Single(c);
        Assert.Equal("ei/real", c[0].Path);
        Assert.Null(c[0].Response);
    }

    [Fact]
    public void NewKeysWinOverLegacy_AtBlockLevel_InBothParsers() {


        const string yaml = """
            routes:
              - path: ei/both
                requestType: OldRequest
                request: NewRequest
                response:  # explicitly empty new key beats legacy below
                responseType: OldResponse
            """;

        var g = RouteParser.Parse(yaml)[0];
        Assert.Equal("NewRequest", g.Request);
        Assert.Null(g.Response);

        var c = RouteCatalog.Parse(yaml)[0];
        Assert.Equal("NewRequest", c.Request);
        Assert.Null(c.Response);
    }
}

using EggIncognito.Services;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests;

public sealed class AuxbrainCatalogTests {
    private static RouteInfo Route(
        string path,
        string? request = "ConfigRequest",
        string? response = "ConfigResponse",
        bool requestWrapped = false,
        bool responseWrapped = false,
        bool pathParam = false,
        IReadOnlyList<string>? aliases = null) =>
        new(path, request, response, requestWrapped, responseWrapped, null, pathParam, false) { Aliases = aliases ?? [] };

    private static CanonicalPath Canonical(
        string? request = "ConfigRequest",
        string? response = "ConfigResponse",
        bool requestWrapped = false,
        bool responseWrapped = false,
        bool pathParam = false) =>
        new(request, response, requestWrapped, responseWrapped, pathParam);

    private static EndpointStatus.Result Status(
        string[]? ok = null, string[]? empty = null, string[]? missing = null) =>
        new(ok ?? [], empty ?? [], missing ?? []);

    [Fact]
    public void Build_UnionsMockOnly_Matched_AndRealUnmocked() {
        var routes = new[] { Route("ei/mock_only"), Route("ei/matched") };
        var canonical = new Dictionary<string, CanonicalPath> {
            ["ei/matched"] = Canonical(),
            ["ei/real_only"] = Canonical("FooRequest", "FooResponse", true)
        };

        var entries = AuxbrainCatalog.Build(routes, canonical, Status());

        Assert.Equal(["ei/matched", "ei/mock_only", "ei/real_only"], entries.Select(e => e.Path));
        Assert.Equal(AuxbrainStatus.Ok, entries.Single(e => e.Path == "ei/matched").Status);
        Assert.Equal(AuxbrainStatus.Ok, entries.Single(e => e.Path == "ei/mock_only").Status);

        var real = entries.Single(e => e.Path == "ei/real_only");
        Assert.Equal(AuxbrainStatus.NotMocked, real.Status);
        Assert.Equal("FooRequest", real.RequestType);
        Assert.Equal("FooResponse", real.ResponseType);
        Assert.True(real.RequestWrapped);
    }

    [Fact]
    public void Build_MapsEndpointStatusBuckets() {
        var routes = new[] { Route("ei/a"), Route("ei/b"), Route("ei/c") };
        var status = Status(["ei/a"], ["ei/b"], ["ei/c"]);

        var entries = AuxbrainCatalog.Build(routes, new Dictionary<string, CanonicalPath>(), status);

        Assert.Equal(AuxbrainStatus.Ok, entries.Single(e => e.Path == "ei/a").Status);
        Assert.Equal(AuxbrainStatus.Empty, entries.Single(e => e.Path == "ei/b").Status);
        Assert.Equal(AuxbrainStatus.Missing, entries.Single(e => e.Path == "ei/c").Status);
    }

    [Fact]
    public void Build_RouteNotInStatusResult_CountsAsOk() {
        var entries = AuxbrainCatalog.Build(
            [Route("ei/raw")], new Dictionary<string, CanonicalPath>(), Status());
        Assert.Equal(AuxbrainStatus.Ok, entries.Single().Status);
    }

    [Fact]
    public void Build_MatchedPath_UsesRouteShapeNotCanonical() {
        var routes = new[] { Route("ei/coop_status", responseWrapped: false) };
        var canonical = new Dictionary<string, CanonicalPath> {
            ["ei/coop_status"] = Canonical(responseWrapped: true)
        };

        var entry = AuxbrainCatalog.Build(routes, canonical, Status()).Single();
        Assert.False(entry.ResponseWrapped);
    }

    [Fact]
    public void Build_PassesAliasesThrough_AndSkipsAliasCoveredCanonicalKeys() {
        var routes = new[] { Route("ei/new_name", aliases: ["ei/old_name"]) };
        var canonical = new Dictionary<string, CanonicalPath> {
            ["ei/new_name"] = Canonical(),
            ["ei/old_name"] = Canonical()
        };

        var entries = AuxbrainCatalog.Build(routes, canonical, Status());

        var entry = Assert.Single(entries);
        Assert.Equal("ei/new_name", entry.Path);
        Assert.Equal(["ei/old_name"], entry.Aliases);
    }

    [Fact]
    public void Build_ExtractsNamespaceFromFirstSegment() {
        var entries = AuxbrainCatalog.Build(
            [Route("ei_ctx/get_leaderboard"), Route("ei/get_config")],
            new Dictionary<string, CanonicalPath>(), Status());

        Assert.Equal("ei_ctx", entries.Single(e => e.Path == "ei_ctx/get_leaderboard").Namespace);
        Assert.Equal("ei", entries.Single(e => e.Path == "ei/get_config").Namespace);
    }

    [Fact]
    public void Label_CoversAllStatuses() {
        Assert.Equal("ok", AuxbrainCatalog.Label(AuxbrainStatus.Ok));
        Assert.Equal("empty", AuxbrainCatalog.Label(AuxbrainStatus.Empty));
        Assert.Equal("missing", AuxbrainCatalog.Label(AuxbrainStatus.Missing));
        Assert.Equal("not-mocked", AuxbrainCatalog.Label(AuxbrainStatus.NotMocked));
    }

    [Fact]
    public void ResolveJsonPath_FindsRepoFile_AndLoadCanonicalParsesIt() {
        string path = AuxbrainCatalog.ResolveJsonPath(new ConfigurationBuilder().Build());
        Assert.True(File.Exists(path), $"auxbrain-paths.json not found at {path}");

        var canonical = AuxbrainCatalog.LoadCanonical(path);
        Assert.True(canonical.Count >= 64, $"expected >= 64 canonical paths, got {canonical.Count}");

        var bot = canonical["ei/coop_status_bot"];
        Assert.Equal("ContractCoopStatusRequest", bot.RequestType);
        Assert.Equal("ContractCoopStatusResponse", bot.ResponseType);
        Assert.False(bot.RequestWrapped);
        Assert.True(bot.ResponseWrapped);
        Assert.False(bot.PathParam);
    }

    [Fact]
    public void ResolveJsonPath_ConfigOverrideWins() {
        string tmp = Path.Combine(Path.GetTempPath(), "egi-axp-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(tmp, "{}");
        try {
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["AuxbrainPathsPath"] = tmp }).Build();
            Assert.Equal(tmp, AuxbrainCatalog.ResolveJsonPath(config));
        } finally {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void LoadCanonical_MissingFile_ReturnsEmpty() {
        var canonical = AuxbrainCatalog.LoadCanonical(
            Path.Combine(Path.GetTempPath(), "egi-axp-none-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Empty(canonical);
    }
}

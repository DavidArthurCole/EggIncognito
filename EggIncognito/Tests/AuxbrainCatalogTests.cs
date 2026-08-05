using EggIncognito.Services;

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

    private static EndpointStatus.Result Status(
        string[]? ok = null, string[]? empty = null, string[]? missing = null) =>
        new(ok ?? [], empty ?? [], missing ?? []);

    [Fact]
    public void Build_MapsEndpointStatusBuckets() {
        var routes = new[] { Route("ei/a"), Route("ei/b"), Route("ei/c") };
        var status = Status(["ei/a"], ["ei/b"], ["ei/c"]);

        var entries = AuxbrainCatalog.Build(routes, status);

        Assert.Equal(AuxbrainStatus.Ok, entries.Single(e => e.Path == "ei/a").Status);
        Assert.Equal(AuxbrainStatus.Empty, entries.Single(e => e.Path == "ei/b").Status);
        Assert.Equal(AuxbrainStatus.Missing, entries.Single(e => e.Path == "ei/c").Status);
    }

    [Fact]
    public void Build_RouteNotInStatusResult_CountsAsOk() {
        var entries = AuxbrainCatalog.Build([Route("ei/raw")], Status());
        Assert.Equal(AuxbrainStatus.Ok, entries.Single().Status);
    }

    [Fact]
    public void Build_PassesAliasesThrough() {
        var routes = new[] { Route("ei/new_name", aliases: ["ei/old_name"]) };

        var entries = AuxbrainCatalog.Build(routes, Status());

        var entry = Assert.Single(entries);
        Assert.Equal("ei/new_name", entry.Path);
        Assert.Equal(["ei/old_name"], entry.Aliases);
    }

    [Fact]
    public void Build_ExtractsNamespaceFromFirstSegment() {
        var entries = AuxbrainCatalog.Build(
            [Route("ei_ctx/get_leaderboard"), Route("ei/get_config")], Status());

        Assert.Equal("ei_ctx", entries.Single(e => e.Path == "ei_ctx/get_leaderboard").Namespace);
        Assert.Equal("ei", entries.Single(e => e.Path == "ei/get_config").Namespace);
    }

    [Fact]
    public void Label_CoversAllStatuses() {
        Assert.Equal("ok", AuxbrainCatalog.Label(AuxbrainStatus.Ok));
        Assert.Equal("empty", AuxbrainCatalog.Label(AuxbrainStatus.Empty));
        Assert.Equal("missing", AuxbrainCatalog.Label(AuxbrainStatus.Missing));
    }
}

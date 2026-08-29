using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public sealed class OverlayRouteCatalogTests {
    private static RouteInfo Route(string path, string? request = "ReqType", string? response = "RespType",
        bool requestWrapped = false, bool responseWrapped = false, bool pathParam = false,
        IReadOnlyList<string>? aliases = null) =>
        new(path, request, response, requestWrapped, responseWrapped, null, pathParam, false) {
            Aliases = aliases ?? []
        };

    private static RouteOverrideInfo Override(string path, string? request = null, string? response = null,
        bool? requestWrapped = null, bool? responseWrapped = null, bool? pathParam = null) =>
        new(path, request, response, requestWrapped, responseWrapped, pathParam, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void Resolve_PerFieldMerge_OverrideWinsOnlyForSetFields() {
        var inner = new FakeCatalog(Route("ei/known", request: "OrigReq", response: "OrigResp",
            requestWrapped: false, responseWrapped: true, pathParam: false));
        var overrides = new FakeOverrides(Override("ei/known", response: "NewResp", pathParam: true));
        var overlay = new OverlayRouteCatalog(inner, overrides);

        var resolved = overlay.Resolve("ei/known")!;
        Assert.Equal("OrigReq", resolved.Request);
        Assert.Equal("NewResp", resolved.Response);
        Assert.False(resolved.RequestWrapped);
        Assert.True(resolved.ResponseWrapped);
        Assert.True(resolved.PathParam);
    }

    [Fact]
    public void Resolve_NoOverrideForPath_ReturnsInnerRouteUnchanged() {
        var inner = new FakeCatalog(Route("ei/known"));
        var overlay = new OverlayRouteCatalog(inner, new FakeOverrides());

        var resolved = overlay.Resolve("ei/known")!;
        Assert.Equal("ReqType", resolved.Request);
        Assert.Equal("RespType", resolved.Response);
    }

    [Fact]
    public void Resolve_UnknownPath_ReturnsNull() {
        var inner = new FakeCatalog(Route("ei/known"));
        var overlay = new OverlayRouteCatalog(inner, new FakeOverrides(Override("ei/unknown", response: "X")));

        Assert.Null(overlay.Resolve("ei/unknown"));
    }

    [Fact]
    public void NullProvider_IsPureDelegation() {
        var inner = new FakeCatalog(Route("ei/known"));
        var overlay = new OverlayRouteCatalog(inner, null);

        Assert.Same(inner.All(), overlay.All());
        Assert.Equal(inner.Resolve("ei/known"), overlay.Resolve("ei/known"));
        Assert.Null(overlay.Resolve("ei/missing"));
    }

    [Fact]
    public void All_MapsEveryRoute_ApplyingOverridesWhereTheyExist() {
        var inner = new FakeCatalog(
            Route("ei/one", response: "One"),
            Route("ei/two", response: "Two"),
            Route("ei/three", response: "Three"));
        var overlay = new OverlayRouteCatalog(inner, new FakeOverrides(Override("ei/two", response: "TwoOverridden")));

        var all = overlay.All();
        Assert.Equal(3, all.Count);
        Assert.Equal("One", all.Single(r => r.Path == "ei/one").Response);
        Assert.Equal("TwoOverridden", all.Single(r => r.Path == "ei/two").Response);
        Assert.Equal("Three", all.Single(r => r.Path == "ei/three").Response);
    }

    [Fact]
    public void All_OrphanOverride_IsIgnored() {
        var inner = new FakeCatalog(Route("ei/one"));
        var overlay = new OverlayRouteCatalog(inner, new FakeOverrides(Override("ei/orphan", response: "X")));

        var all = overlay.All();
        Assert.Single(all);
        Assert.Equal("ei/one", all[0].Path);
    }

    [Fact]
    public void Resolve_Merge_PreservesAliases() {
        var inner = new FakeCatalog(Route("ei/known", aliases: ["ei/legacy", "ei/alt"]));
        var overlay = new OverlayRouteCatalog(inner,
            new FakeOverrides(Override("ei/known", response: "NewResp")));

        var resolved = overlay.Resolve("ei/known")!;
        Assert.Equal(["ei/legacy", "ei/alt"], resolved.Aliases);
    }

    private sealed class FakeCatalog(params RouteInfo[] routes) : IRouteCatalog {
        private readonly Dictionary<string, RouteInfo> _map = routes.ToDictionary(r => r.Path, StringComparer.Ordinal);
        public IReadOnlyList<RouteInfo> All() => routes;
        public RouteInfo? Resolve(string path) => _map.GetValueOrDefault(path);
    }

    private sealed class FakeOverrides(params RouteOverrideInfo[] overrides) : IRouteOverrideProvider {
        private readonly Dictionary<string, RouteOverrideInfo> _map =
            overrides.ToDictionary(o => o.Path, StringComparer.Ordinal);
        public IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot() => _map;
        public void Invalidate() {
        }
    }
}

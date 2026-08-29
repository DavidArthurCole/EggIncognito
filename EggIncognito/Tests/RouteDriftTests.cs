using EggIncognito.Core.Services;
namespace EggIncognito.Tests;

public sealed class RouteDriftTests {
    private static RouteInfo Effective(string path, bool requestWrapped = false, bool responseWrapped = false) =>
        new(path, "X", "Y", requestWrapped, responseWrapped, null, false, false);

    private static BinaryRouteInfo Binary(string path, bool requestWrapped = false, bool responseWrapped = false) =>
        new(path, "getX", "X", "Y", requestWrapped, responseWrapped, "1.37", null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void RequestWrappedMismatch_IsReliableDrift() {
        var drift = RouteDrift.Compute([Effective("ei/known", requestWrapped: false)],
            [Binary("ei/known", requestWrapped: true)]);

        var row = Assert.Single(drift);
        Assert.Equal("requestWrapped", row.Field);
        Assert.True(row.Reliable);
    }

    [Fact]
    public void ResponseWrappedMismatch_IsAdvisoryDrift() {
        var drift = RouteDrift.Compute([Effective("ei/known", responseWrapped: false)],
            [Binary("ei/known", responseWrapped: true)]);

        var row = Assert.Single(drift);
        Assert.Equal("responseWrapped", row.Field);
        Assert.False(row.Reliable);
    }

    [Fact]
    public void BinaryOnlyEndpoint_IsFlaggedAsNew() {
        var drift = RouteDrift.Compute([], [Binary("ei/discovered")]);

        var row = Assert.Single(drift);
        Assert.Equal("ei/discovered", row.Path);
        Assert.Equal("new", row.Field);
    }

    [Fact]
    public void MatchingRoute_NoDrift() {
        var drift = RouteDrift.Compute([Effective("ei/known")], [Binary("ei/known")]);
        Assert.Empty(drift);
    }
}

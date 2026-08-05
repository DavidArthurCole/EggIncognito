using EggIncognito.Services;

namespace EggIncognito.Tests;

public sealed class RouteOverrideCacheTests {
    private static RouteOverrideInfo Info(string path) =>
        new(path, "ReqType", "RespType", true, false, null, DateTimeOffset.UnixEpoch, null);

    private static Dictionary<string, RouteOverrideInfo> Dict(params RouteOverrideInfo[] infos) =>
        infos.ToDictionary(i => i.Path, StringComparer.Ordinal);

    [Fact]
    public void Snapshot_BeforeTtlElapses_DoesNotRefetch() {
        var time = new FakeTime();
        int calls = 0;
        var provider = new CachedRouteOverrideProvider(() => {
            calls++;
            return Dict(Info("a"));
        }, TimeSpan.FromSeconds(10), time);

        provider.Snapshot();
        provider.Snapshot();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Snapshot_AfterTtlElapses_Refetches() {
        var time = new FakeTime();
        int calls = 0;
        var provider = new CachedRouteOverrideProvider(() => {
            calls++;
            return Dict(Info("a"));
        }, TimeSpan.FromSeconds(10), time);

        provider.Snapshot();
        time.Advance(TimeSpan.FromSeconds(11));
        provider.Snapshot();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Invalidate_ForcesRefetch_EvenWithinTtl() {
        var time = new FakeTime();
        int calls = 0;
        var provider = new CachedRouteOverrideProvider(() => {
            calls++;
            return Dict(Info("a"));
        }, TimeSpan.FromSeconds(10), time);

        provider.Snapshot();
        provider.Invalidate();
        provider.Snapshot();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Snapshot_FetchThrowsAfterSuccess_KeepsStaleSnapshot() {
        var time = new FakeTime();
        bool fail = false;
        var provider = new CachedRouteOverrideProvider(() => {
            if (fail) throw new InvalidOperationException("db down");
            return Dict(Info("a"));
        }, TimeSpan.FromSeconds(10), time);

        var first = provider.Snapshot();
        Assert.True(first.ContainsKey("a"));

        fail = true;
        provider.Invalidate();
        var second = provider.Snapshot();
        Assert.Same(first, second);
        Assert.True(second.ContainsKey("a"));
    }

    [Fact]
    public void Snapshot_FirstFetchThrows_YieldsEmptyDict() {
        var time = new FakeTime();
        var provider = new CachedRouteOverrideProvider(
            () => throw new InvalidOperationException("db down"),
            TimeSpan.FromSeconds(10), time);

        var snapshot = provider.Snapshot();
        Assert.Empty(snapshot);
    }

    [Fact]
    public void Snapshot_KeysByInfoPath_NotFetchDictKey() {
        var time = new FakeTime();
        var mismatched = new Dictionary<string, RouteOverrideInfo> {
            ["wrong-key"] = Info("actual/path")
        };
        var provider = new CachedRouteOverrideProvider(() => mismatched, TimeSpan.FromSeconds(10), time);

        var snapshot = provider.Snapshot();
        Assert.True(snapshot.ContainsKey("actual/path"));
        Assert.False(snapshot.ContainsKey("wrong-key"));
    }

    private sealed class FakeTime : TimeProvider {
        private DateTimeOffset _now = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan d) => _now += d;
    }
}

using EggIncognito.Services.Metrics;

namespace EggIncognito.Tests;

public class ApiAuditLogTests
{
    private static ApiAuditLog New() => new(TimeProvider.System);

    [Fact]
    public void Record_UpdatesBucketTotals()
    {
        var a = New();
        a.Record("GET", "/api/x", 200, RequestBucket.Internal, "1.1.1.1", null);
        a.Record("GET", "/api/x", 200, RequestBucket.Cross, "1.1.1.1", null);
        a.Record("GET", "/api/y", 404, RequestBucket.External, "2.2.2.2", null);
        var (i, c, e) = a.Buckets();
        Assert.Equal(1, i);
        Assert.Equal(1, c);
        Assert.Equal(1, e);
    }

    [Fact]
    public void Paths_RollupSplitsByBucket_SortedByTotal()
    {
        var a = New();
        for (var n = 0; n < 3; n++) a.Record("GET", "/api/hot", 200, RequestBucket.External, "1.1.1.1", null);
        a.Record("GET", "/api/cold", 200, RequestBucket.Internal, "1.1.1.1", null);

        var paths = a.Paths();
        Assert.Equal("/api/hot", paths[0].Path);
        Assert.Equal(3, paths[0].Roll.Total);
        Assert.Equal(3, paths[0].Roll.External);
        var cold = paths.First(p => p.Path == "/api/cold");
        Assert.Equal(1, cold.Roll.Internal);
    }

    [Fact]
    public void Ips_TracksDistinctPaths()
    {
        var a = New();
        a.Record("GET", "/api/a", 200, RequestBucket.External, "9.9.9.9", null);
        a.Record("GET", "/api/b", 200, RequestBucket.External, "9.9.9.9", null);
        a.Record("GET", "/api/a", 200, RequestBucket.External, "9.9.9.9", null);
        var ip = a.Ips().Single();
        Assert.Equal("9.9.9.9", ip.Ip);
        Assert.Equal(3, ip.Total);
        Assert.Equal(2, ip.DistinctPaths);
    }

    [Fact]
    public void Recent_NewestFirst_Bounded()
    {
        var a = New();
        for (var n = 0; n < ApiAuditLog.RecentCapacity + 50; n++)
            a.Record("GET", $"/api/{n}", 200, RequestBucket.External, "1.1.1.1", null);

        var recent = a.Recent(1000);
        Assert.Equal(ApiAuditLog.RecentCapacity, recent.Count);
        var last = ApiAuditLog.RecentCapacity + 50 - 1;
        Assert.Equal($"/api/{last}", recent[0].Path);
    }

    [Fact]
    public void Recent_TakeClampsToRequested()
    {
        var a = New();
        for (var n = 0; n < 20; n++) a.Record("GET", "/api/x", 200, RequestBucket.External, "1.1.1.1", null);
        Assert.Equal(5, a.Recent(5).Count);
    }
}

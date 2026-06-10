using EggIncognito.Data.Services;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class RouteSeederTests
{
    private static RouteInfo R(string path, string? req, string? resp)
        => new(path, req, resp, false, false, null, false, false);

    [Fact]
    public void ToRow_MapsAllFields()
    {
        var row = RouteSeeder.ToYamlRow(R("ei/x", "Req", "Resp"));
        Assert.Equal("ei/x", row.Path);
        Assert.Equal("Resp", row.ResponseType);
        Assert.Equal("yaml", row.Source);
    }

    [Fact]
    public void NeedsUpdate_TrueWhenTypeDrifted()
    {
        var existing = RouteSeeder.ToYamlRow(R("ei/x", "Req", "OldResp"));
        Assert.True(RouteSeeder.NeedsUpdate(existing, R("ei/x", "Req", "NewResp")));
    }

    [Fact]
    public void NeedsUpdate_FalseWhenSame()
    {
        var existing = RouteSeeder.ToYamlRow(R("ei/x", "Req", "Resp"));
        Assert.False(RouteSeeder.NeedsUpdate(existing, R("ei/x", "Req", "Resp")));
    }

    [Fact]
    public void Apply_UpdatesDriftedColumns()
    {
        var row = RouteSeeder.ToYamlRow(R("ei/x", "Req", "OldResp"));
        RouteSeeder.Apply(row, R("ei/x", "Req2", "NewResp"));
        Assert.Equal("Req2", row.RequestType);
        Assert.Equal("NewResp", row.ResponseType);
    }
}

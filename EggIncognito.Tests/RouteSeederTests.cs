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

    [Fact]
    public void Plan_InsertsMissingYamlRoutes()
    {
        var existing = RouteSeeder.ToYamlRow(R("ei/a", "Req", "Resp"));
        var toAdd = RouteSeeder.Plan([existing], [R("ei/a", "Req", "Resp"), R("ei/b", "Req2", "Resp2")]);
        var added = Assert.Single(toAdd);
        Assert.Equal("ei/b", added.Path);
        Assert.Equal("yaml", added.Source);
    }

    [Fact]
    public void Plan_UpdatesDriftedYamlRowInPlace_NoInsert()
    {
        var row = RouteSeeder.ToYamlRow(R("ei/a", "Req", "OldResp"));
        var toAdd = RouteSeeder.Plan([row], [R("ei/a", "Req", "NewResp")]);
        Assert.Empty(toAdd);
        Assert.Equal("NewResp", row.ResponseType);
    }

    [Fact]
    public void Plan_UndriftedYamlRow_NoInsertNoChange()
    {
        var row = RouteSeeder.ToYamlRow(R("ei/a", "Req", "Resp"));
        var toAdd = RouteSeeder.Plan([row], [R("ei/a", "Req", "Resp")]);
        Assert.Empty(toAdd);
        Assert.Equal("Resp", row.ResponseType);
    }

    [Fact]
    public void Plan_NeverTouchesDbSourceRows()
    {
        var dbRow = RouteSeeder.ToYamlRow(R("ei/a", "DbReq", "DbResp"));
        dbRow.Source = "db";
        var toAdd = RouteSeeder.Plan([dbRow], [R("ei/a", "Req", "Resp")]);
        Assert.Equal("DbReq", dbRow.RequestType);
        Assert.Equal("DbResp", dbRow.ResponseType);
        Assert.Equal("db", dbRow.Source);
        var added = Assert.Single(toAdd);
        Assert.Equal("yaml", added.Source);
    }
}

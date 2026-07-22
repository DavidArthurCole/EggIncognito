using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class MigrationSnapshotTests {
    [Fact]
    public void Context_BuildsModel_WithBothTables() {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=designtime").Options;
        using var ctx = new EggIncognitoDbContext(options);
        var tables = ctx.Model.GetEntityTypes().Select(t => t.GetTableName()).ToHashSet();
        Assert.Contains("stored_endpoints", tables);
        Assert.Contains("stored_routes", tables);
    }
}

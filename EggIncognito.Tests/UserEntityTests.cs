using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class UserEntityTests
{
    [Fact]
    public void Model_HasUsersTable()
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=designtime").Options;
        using var ctx = new EggIncognitoDbContext(options);
        var tables = ctx.Model.GetEntityTypes().Select(t => t.GetTableName()).ToHashSet();
        Assert.Contains("users", tables);
    }

    [Fact]
    public void User_HasRoleColumn()
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=designtime").Options;
        using var ctx = new EggIncognitoDbContext(options);
        var userType = ctx.Model.FindEntityType(typeof(EggIncognito.Data.Models.User))!;
        Assert.NotNull(userType.FindProperty(nameof(EggIncognito.Data.Models.User.Role)));
    }
}

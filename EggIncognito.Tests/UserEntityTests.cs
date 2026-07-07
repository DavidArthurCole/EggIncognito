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

    [Fact]
    public void User_UserIdIsPrimaryKey_DiscordIdIsNullable()
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=designtime").Options;
        using var ctx = new EggIncognitoDbContext(options);
        var userType = ctx.Model.FindEntityType(typeof(EggIncognito.Data.Models.User))!;
        var pk = userType.FindPrimaryKey()!;
        Assert.Equal([nameof(EggIncognito.Data.Models.User.UserId)], pk.Properties.Select(p => p.Name));
        var discordId = userType.FindProperty(nameof(EggIncognito.Data.Models.User.DiscordId))!;
        Assert.True(discordId.IsNullable);
    }

    [Fact]
    public void Model_HasIdentitiesTable_KeyedByProviderAndSubject()
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=designtime").Options;
        using var ctx = new EggIncognitoDbContext(options);
        var tables = ctx.Model.GetEntityTypes().Select(t => t.GetTableName()).ToHashSet();
        Assert.Contains("identities", tables);

        var identityType = ctx.Model.FindEntityType(typeof(EggIncognito.Data.Models.Identity))!;
        var pk = identityType.FindPrimaryKey()!;
        Assert.Equal(
            [nameof(EggIncognito.Data.Models.Identity.Provider), nameof(EggIncognito.Data.Models.Identity.Subject)],
            pk.Properties.Select(p => p.Name));
    }

    [Fact]
    public void CaptureProxyAddrAndCaptureUserCa_HaveUserIdColumn()
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=designtime").Options;
        using var ctx = new EggIncognitoDbContext(options);

        var proxyAddrType = ctx.Model.FindEntityType(typeof(EggIncognito.Data.Models.CaptureProxyAddr))!;
        Assert.NotNull(proxyAddrType.FindProperty(nameof(EggIncognito.Data.Models.CaptureProxyAddr.UserId)));
        Assert.Equal(
            [nameof(EggIncognito.Data.Models.CaptureProxyAddr.DiscordId)],
            proxyAddrType.FindPrimaryKey()!.Properties.Select(p => p.Name));

        var userCaType = ctx.Model.FindEntityType(typeof(EggIncognito.Data.Models.CaptureUserCa))!;
        Assert.NotNull(userCaType.FindProperty(nameof(EggIncognito.Data.Models.CaptureUserCa.UserId)));
        Assert.Equal(
            [nameof(EggIncognito.Data.Models.CaptureUserCa.DiscordId)],
            userCaType.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }
}

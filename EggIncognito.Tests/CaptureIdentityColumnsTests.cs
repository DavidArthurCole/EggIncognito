using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class CaptureIdentityColumnsTests
{
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

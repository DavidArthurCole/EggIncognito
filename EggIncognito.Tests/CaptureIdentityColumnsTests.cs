using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class CaptureIdentityColumnsTests
{
    [Fact]
    public void CaptureProxyAddrAndCaptureUserCa_HaveUserIdPrimaryKey()
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=designtime").Options;
        using var ctx = new EggIncognitoDbContext(options);

        var proxyAddrType = ctx.Model.FindEntityType(typeof(EggIncognito.Data.Models.CaptureProxyAddr))!;
        Assert.Equal(
            [nameof(EggIncognito.Data.Models.CaptureProxyAddr.UserId)],
            proxyAddrType.FindPrimaryKey()!.Properties.Select(p => p.Name));

        var userCaType = ctx.Model.FindEntityType(typeof(EggIncognito.Data.Models.CaptureUserCa))!;
        Assert.Equal(
            [nameof(EggIncognito.Data.Models.CaptureUserCa.UserId)],
            userCaType.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }
}

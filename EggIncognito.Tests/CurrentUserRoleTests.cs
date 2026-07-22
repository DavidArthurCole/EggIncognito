using System.Security.Claims;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using SyncKit.Contract;

namespace EggIncognito.Tests;

public class CurrentUserRoleTests
{
    private static CurrentUser Make(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        if (claims.Length > 0) ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Discord"));
        return new CurrentUser(new HttpContextAccessor { HttpContext = ctx });
    }

    [Fact]
    public void Anonymous_IsViewer()
    {
        var u = Make();
        Assert.Equal(UserRole.Viewer, u.Role);
        Assert.False(u.IsAtLeast(UserRole.Contributor));
    }

    [Fact]
    public void Contributor_Claim_IsAtLeastContributor()
    {
        var u = Make(new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(AuthClaims.RoleClaim, "contributor"));
        Assert.Equal(UserRole.Contributor, u.Role);
        Assert.True(u.IsAtLeast(UserRole.Contributor));
        Assert.False(u.IsAtLeast(UserRole.Admin));
    }

    [Fact]
    public void Admin_Claim_IsAtLeastAdmin()
    {
        var u = Make(new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(AuthClaims.RoleClaim, "admin"));
        Assert.True(u.IsAtLeast(UserRole.Admin));
    }
}

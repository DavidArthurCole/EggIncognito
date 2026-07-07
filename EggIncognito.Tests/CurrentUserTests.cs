using System.Security.Claims;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Tests;

public class CurrentUserTests
{
    private static CurrentUser Make(ClaimsPrincipal? principal)
    {
        var ctx = new DefaultHttpContext();
        if (principal is not null) ctx.User = principal;
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new CurrentUser(accessor);
    }

    [Fact]
    public void Anonymous_IsNotAuthenticated()
    {
        var u = Make(null);
        Assert.False(u.IsAuthenticated);
        Assert.Null(u.DiscordId);
    }

    [Fact]
    public void Authenticated_ExposesClaims()
    {
        var id = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "123"), new Claim(ClaimTypes.Name, "alice"),
             new Claim("urn:discord:avatar:hash", "abc")], "Discord");
        var u = Make(new ClaimsPrincipal(id));
        Assert.True(u.IsAuthenticated);
        Assert.Equal("123", u.DiscordId);
        Assert.Equal("alice", u.Username);
        Assert.Equal("abc", u.Avatar);
    }

    [Fact]
    public void Authenticated_ExposesUserId()
    {
        var guid = Guid.NewGuid();
        var id = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "123"), new Claim(EggIncognito.Data.Services.AuthClaims.UserIdClaim, guid.ToString())], "Discord");
        var u = Make(new ClaimsPrincipal(id));
        Assert.Equal(guid, u.UserId);
    }

    [Fact]
    public void Authenticated_NoUserIdClaim_UserIdIsNull()
    {
        var id = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "123")], "Discord");
        var u = Make(new ClaimsPrincipal(id));
        Assert.Null(u.UserId);
    }
}

using EggIncognito.Services;
using EggIncognito.Services.RateLimiting;
using EggIncognito.Data.Models;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Tests;

public class RateLimitKeysTests
{
    private static HttpContext CtxWith(string? cfIp = null, string? xff = null, string? remote = "10.0.0.9")
    {
        var ctx = new DefaultHttpContext();
        if (cfIp is not null) ctx.Request.Headers["CF-Connecting-IP"] = cfIp;
        if (xff is not null) ctx.Request.Headers["X-Forwarded-For"] = xff;
        if (remote is not null) ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remote);
        return ctx;
    }

    [Fact]
    public void ClientIp_PrefersCfHeader()
    {
        Assert.Equal("1.2.3.4", RateLimitKeys.ClientIp(CtxWith(cfIp: "1.2.3.4", xff: "9.9.9.9")));
    }

    [Fact]
    public void ClientIp_FallsBackToXffFirstHop_ThenRemote()
    {
        Assert.Equal("9.9.9.9", RateLimitKeys.ClientIp(CtxWith(cfIp: null, xff: "9.9.9.9, 8.8.8.8")));
        Assert.Equal("10.0.0.9", RateLimitKeys.ClientIp(CtxWith(cfIp: null, xff: null)));
    }

    [Fact]
    public void PartitionKey_UsesUser_WhenAuthenticated()
    {
        var ctx = CtxWith(cfIp: "1.2.3.4");
        var user = new FakeUser(authenticated: true, id: "disc123", role: UserRole.Viewer);
        Assert.Equal("user:disc123", RateLimitKeys.PartitionKey(ctx, user));
    }

    [Fact]
    public void PartitionKey_UsesIp_WhenAnonymous()
    {
        var ctx = CtxWith(cfIp: "1.2.3.4");
        var user = new FakeUser(authenticated: false, id: null, role: UserRole.Viewer);
        Assert.Equal("ip:1.2.3.4", RateLimitKeys.PartitionKey(ctx, user));
    }

    [Theory]
    [InlineData(false, UserRole.Viewer, "Anon")]
    [InlineData(true, UserRole.Viewer, "Viewer")]
    [InlineData(true, UserRole.Contributor, "Contributor")]
    [InlineData(true, UserRole.Admin, "Contributor")]
    public void TierFor_MapsRole(bool auth, UserRole role, string expected)
    {
        Assert.Equal(expected, RateLimitKeys.TierFor(new FakeUser(auth, auth ? "x" : null, role)));
    }

    private sealed class FakeUser(bool authenticated, string? id, UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? DiscordId => id;
        public string? Username => null;
        public string? Avatar => null;
        public UserRole Role => role;
        public bool IsAtLeast(UserRole need) => role >= need;
    }
}

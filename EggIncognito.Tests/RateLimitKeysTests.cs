using EggIncognito.Services;
using EggIncognito.Services.RateLimiting;
using EggIncognito.Data.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

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
        Assert.Equal("1.2.3.4", RateLimitKeys.ClientIp(CtxWith(cfIp: "1.2.3.4", xff: "9.9.9.9"), hosted: false));
        Assert.Equal("1.2.3.4", RateLimitKeys.ClientIp(CtxWith(cfIp: "1.2.3.4", xff: "9.9.9.9"), hosted: true));
    }

    [Fact]
    public void ClientIp_Local_FallsBackToXffFirstHop_ThenRemote()
    {
        Assert.Equal("9.9.9.9", RateLimitKeys.ClientIp(CtxWith(cfIp: null, xff: "9.9.9.9, 8.8.8.8"), hosted: false));
        Assert.Equal("10.0.0.9", RateLimitKeys.ClientIp(CtxWith(cfIp: null, xff: null), hosted: false));
    }

    // Hosted never trusts X-Forwarded-For: a client rotating that header must not mint fresh
    // partitions, so everything without CF-Connecting-IP shares one bucket.
    [Fact]
    public void ClientIp_Hosted_IgnoresXff_UsesSharedBucket()
    {
        Assert.Equal(RateLimitKeys.NoCfKey, RateLimitKeys.ClientIp(CtxWith(cfIp: null, xff: "9.9.9.9"), hosted: true));
        Assert.Equal(RateLimitKeys.NoCfKey, RateLimitKeys.ClientIp(CtxWith(cfIp: null, xff: "8.8.8.8"), hosted: true));
        Assert.Equal(RateLimitKeys.NoCfKey, RateLimitKeys.ClientIp(CtxWith(cfIp: null, xff: null), hosted: true));
    }

    [Fact]
    public void PartitionKey_UsesUser_WhenAuthenticated()
    {
        var ctx = CtxWith(cfIp: "1.2.3.4");
        var userId = Guid.NewGuid();
        var user = new FakeUser(authenticated: true, id: "disc123", role: UserRole.Viewer, userId: userId);
        Assert.Equal($"user:{userId}", RateLimitKeys.PartitionKey(ctx, user, hosted: false));
    }

    [Fact]
    public void PartitionKey_UsesIp_WhenAnonymous()
    {
        var ctx = CtxWith(cfIp: "1.2.3.4");
        var user = new FakeUser(authenticated: false, id: null, role: UserRole.Viewer);
        Assert.Equal("ip:1.2.3.4", RateLimitKeys.PartitionKey(ctx, user, hosted: false));
    }

    [Fact]
    public void PartitionKey_Hosted_Anonymous_NoCf_SharesBucket()
    {
        var ctx = CtxWith(cfIp: null, xff: "6.6.6.6");
        var user = new FakeUser(authenticated: false, id: null, role: UserRole.Viewer);
        Assert.Equal($"ip:{RateLimitKeys.NoCfKey}", RateLimitKeys.PartitionKey(ctx, user, hosted: true));
    }

    [Theory]
    [InlineData(false, UserRole.Viewer, "Anon")]
    [InlineData(true, UserRole.Viewer, "Viewer")]
    [InlineData(true, UserRole.Contributor, "Contributor")]
    [InlineData(true, UserRole.Admin, "Contributor")]
    public void TiersFor_MapsRole(bool auth, UserRole role, string expected)
    {
        Assert.Equal(new[] { expected }, RateLimitKeys.TiersFor(new FakeUser(auth, auth ? "x" : null, role)));
    }

    [Fact]
    public void TiersFor_SupporterViewer_IncludesSupporter()
    {
        var user = new FakeUser(authenticated: true, id: "x", role: UserRole.Viewer, supporter: true);
        Assert.Equal(new[] { "Viewer", "Supporter" }, RateLimitKeys.TiersFor(user));
    }

    [Fact]
    public void TiersFor_NonSupporterContributor_BaseOnly()
    {
        var user = new FakeUser(authenticated: true, id: "x", role: UserRole.Contributor);
        Assert.Equal(new[] { "Contributor" }, RateLimitKeys.TiersFor(user));
    }

    // Defaults: Egress 10, Write 60; tiers Anon 30 / Viewer 120 / Contributor 600.
    [Theory]
    [InlineData("Anon", "Egress", 10)]
    [InlineData("Viewer", "Egress", 10)]
    [InlineData("Contributor", "Egress", 10)]
    [InlineData("Anon", "Write", 30)]
    [InlineData("Viewer", "Write", 60)]
    [InlineData("Contributor", "Write", 60)]
    public void EffectivePermit_IsMinOfPolicyAndTier(string tier, string policy, int expected)
    {
        Assert.Equal(expected, RateLimiterSetup.EffectivePermit(RateLimitOptions.Defaults(), new[] { tier }, policy));
    }

    [Theory]
    [InlineData(false, UserRole.Viewer, false)]
    [InlineData(true, UserRole.Viewer, false)]
    [InlineData(true, UserRole.Contributor, false)]
    [InlineData(true, UserRole.Admin, true)]
    public void IsExempt_OnlyAdmins(bool auth, UserRole role, bool expected)
    {
        Assert.Equal(expected, RateLimiterSetup.IsExempt(new FakeUser(auth, auth ? "x" : null, role)));
    }

    [Fact]
    public void EffectivePermit_TakesBestTier()
    {
        var opts = RateLimitOptions.Defaults();
        var permitSupporter = RateLimiterSetup.EffectivePermit(opts, new[] { "Viewer", "Supporter" }, "Read");
        var permitViewer = RateLimiterSetup.EffectivePermit(opts, new[] { "Viewer" }, "Read");
        Assert.True(permitSupporter >= permitViewer);
    }

    // Sliding-window QueueLimit=0 leases never carry RetryAfter metadata, so the 429 fallback comes
    // from the matched policy's window, not a hardcoded 60.
    [Fact]
    public void FallbackRetryAfter_UsesMatchedPolicyWindow()
    {
        var opts = new RateLimitOptions(
            Enabled: true,
            Tiers: new Dictionary<string, RateLimit> { ["Anon"] = new(30, 60, 6) },
            Policies: new Dictionary<string, RateLimit> { ["Egress"] = new(10, 33, 6) });

        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(null,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("egress")), "test"));
        Assert.Equal(33, RateLimiterSetup.FallbackRetryAfterSeconds(ctx, opts));
    }

    [Fact]
    public void FallbackRetryAfter_Is60_WithoutPolicyMetadata()
    {
        var opts = RateLimitOptions.Defaults();
        Assert.Equal(60, RateLimiterSetup.FallbackRetryAfterSeconds(new DefaultHttpContext(), opts));

        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(null,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("nonesuch")), "test"));
        Assert.Equal(60, RateLimiterSetup.FallbackRetryAfterSeconds(ctx, opts));
    }

    private sealed class FakeUser(bool authenticated, string? id, UserRole role, bool supporter = false, Guid? userId = null) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public Guid? UserId => userId;
        public string? DiscordId => id;
        public string? Username => null;
        public string? Avatar => null;
        public UserRole Role => role;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => role >= need;
    }
}

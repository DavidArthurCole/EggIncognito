using System.Security.Claims;
using EggIncognito.Services;
using EggIncognito.Services.Metrics;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Tests;

public class RequestBucketClassifierTests
{
    private static (HttpContext Ctx, ICurrentUser User) Make(
        bool selfCall = false, bool authed = false, string? origin = null, string? referer = null, string host = "egi.example")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        if (selfCall) ctx.Request.Headers[RequestBucketClassifier.SelfCallHeader] = "1";
        if (origin is not null) ctx.Request.Headers.Origin = origin;
        if (referer is not null) ctx.Request.Headers.Referer = referer;
        if (authed)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "123")], "Discord"));
        var user = new CurrentUser(new HttpContextAccessor { HttpContext = ctx });
        return (ctx, user);
    }

    [Fact]
    public void SelfCallMarker_IsInternal()
    {
        var (ctx, user) = Make(selfCall: true, authed: true);
        Assert.Equal(RequestBucket.Internal, RequestBucketClassifier.Classify(ctx, user));
    }

    [Fact]
    public void SelfCallWins_OverEverythingElse()
    {
        var (ctx, user) = Make(selfCall: true, origin: "https://egi.example");
        Assert.Equal(RequestBucket.Internal, RequestBucketClassifier.Classify(ctx, user));
    }

    [Fact]
    public void Authenticated_NoMarker_IsCross()
    {
        var (ctx, user) = Make(authed: true);
        Assert.Equal(RequestBucket.Cross, RequestBucketClassifier.Classify(ctx, user));
    }

    [Fact]
    public void SameOriginBrowser_Anonymous_IsCross()
    {
        var (ctx, user) = Make(origin: "https://egi.example");
        Assert.Equal(RequestBucket.Cross, RequestBucketClassifier.Classify(ctx, user));
    }

    [Fact]
    public void SameOriginReferer_Anonymous_IsCross()
    {
        var (ctx, user) = Make(referer: "https://egi.example/inspector");
        Assert.Equal(RequestBucket.Cross, RequestBucketClassifier.Classify(ctx, user));
    }

    [Fact]
    public void ForeignOrigin_Anonymous_IsExternal()
    {
        var (ctx, user) = Make(origin: "https://evil.example");
        Assert.Equal(RequestBucket.External, RequestBucketClassifier.Classify(ctx, user));
    }

    [Fact]
    public void NoSessionNoOrigin_IsExternal()
    {
        var (ctx, user) = Make();
        Assert.Equal(RequestBucket.External, RequestBucketClassifier.Classify(ctx, user));
    }
}

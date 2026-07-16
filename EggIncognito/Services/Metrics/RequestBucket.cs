using EggIncognito.Services.RateLimiting;
using Microsoft.AspNetCore.Http;
using SyncKit.Identity.Client;

namespace EggIncognito.Services.Metrics;
public enum RequestBucket
{
   
    Internal,
   
    Cross,
   
    External,
}

public static class RequestBucketClassifier
{
   
    public const string SelfCallHeader = "X-EGI-Internal";

    public static RequestBucket Classify(HttpContext ctx, ICurrentUser user)
    {
        if (ctx.Request.Headers.ContainsKey(SelfCallHeader)) return RequestBucket.Internal;
        if (user.IsAuthenticated) return RequestBucket.Cross;
        if (SameOrigin(ctx)) return RequestBucket.Cross;
        return RequestBucket.External;
    }

   
    private static bool SameOrigin(HttpContext ctx)
    {
        var host = ctx.Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return false;

        var origin = ctx.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && HostMatches(origin, host)) return true;

        var referer = ctx.Request.Headers.Referer.ToString();
        return !string.IsNullOrEmpty(referer) && HostMatches(referer, host);
    }

    private static bool HostMatches(string url, string host) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase);

    public static string Ip(HttpContext ctx, bool hosted) => RateLimitKeys.ClientIp(ctx, hosted);
}

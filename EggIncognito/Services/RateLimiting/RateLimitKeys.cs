using EggIncognito.Data.Models;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Services.RateLimiting;

// Pure helpers that derive the rate-limit partition key + tier from a request. Behind Cloudflare the
// real client IP arrives in CF-Connecting-IP, so that wins. Hosted trusts only that header: X-Forwarded-For
// is client-spoofable, so a Hosted request without CF-Connecting-IP falls into one shared bucket
// instead of letting a header-rotating client mint fresh partitions. Local keeps the XFF first hop
// and the socket IP as fallbacks.
public static class RateLimitKeys
{
    // The shared Hosted partition for requests that did not come through Cloudflare.
    internal const string NoCfKey = "no-cf";

    public static string ClientIp(HttpContext ctx, bool hosted)
    {
        var cf = ctx.Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();

        if (hosted) return NoCfKey;

        var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(xff))
            return xff.Split(',')[0].Trim();

        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Authenticated callers are limited per user id; anonymous callers per client IP.
    public static string PartitionKey(HttpContext ctx, ICurrentUser user, bool hosted) =>
        user.IsAuthenticated && !string.IsNullOrEmpty(user.DiscordId)
            ? $"user:{user.DiscordId}"
            : $"ip:{ClientIp(ctx, hosted)}";

    // The tier name (matches RateLimitOptions.Tiers keys). Contributor + Admin share the top tier.
    public static string TierFor(ICurrentUser user)
    {
        if (!user.IsAuthenticated) return "Anon";
        return user.IsAtLeast(UserRole.Contributor) ? "Contributor" : "Viewer";
    }
}

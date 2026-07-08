using EggIncognito.Data.Models;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Services.RateLimiting;

// Pure helpers that derive the rate-limit partition key + tier from a request. Behind Cloudflare the
// real client IP arrives in CF-Connecting-IP, so that wins. Hosted trusts only that header (X-Forwarded-For
// is client-spoofable); a Hosted request without CF-Connecting-IP falls into one shared bucket. Local
// keeps the XFF first hop and the socket IP as fallbacks.
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

    // Authenticated callers are limited per user id; anonymous callers per client IP. Keys on the
    // provider-neutral UserId (not DiscordId) so an Authentik-only user still gets their own bucket.
    public static string PartitionKey(HttpContext ctx, ICurrentUser user, bool hosted) =>
        user.IsAuthenticated && user.UserId is { } userId
            ? $"user:{userId}"
            : $"ip:{ClientIp(ctx, hosted)}";

    // Tier names that apply to this caller. The effective permit is the best of these, so a supporter
    // contributor keeps whichever limit is higher.
    public static IReadOnlyList<string> TiersFor(ICurrentUser user)
    {
        if (!user.IsAuthenticated) return ["Anon"];
        var baseTier = user.IsAtLeast(UserRole.Contributor) ? "Contributor" : "Viewer";
        return user.IsSupporter ? [baseTier, "Supporter"] : [baseTier];
    }
}

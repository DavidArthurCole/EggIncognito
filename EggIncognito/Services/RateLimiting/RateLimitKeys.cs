using EggIncognito.Data.Models;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Services.RateLimiting;

// Pure helpers that derive the rate-limit partition key + tier from a request. Behind Cloudflare the
// real client IP arrives in CF-Connecting-IP (only Cloudflare reaches the origin), so that wins;
// X-Forwarded-For first hop and the socket IP are local/no-CF fallbacks.
public static class RateLimitKeys
{
    public static string ClientIp(HttpContext ctx)
    {
        var cf = ctx.Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();

        var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(xff))
            return xff.Split(',')[0].Trim();

        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Authenticated callers are limited per user id; anonymous callers per client IP.
    public static string PartitionKey(HttpContext ctx, ICurrentUser user) =>
        user.IsAuthenticated && !string.IsNullOrEmpty(user.DiscordId)
            ? $"user:{user.DiscordId}"
            : $"ip:{ClientIp(ctx)}";

    // The tier name (matches RateLimitOptions.Tiers keys). Contributor + Admin share the top tier.
    public static string TierFor(ICurrentUser user)
    {
        if (!user.IsAuthenticated) return "Anon";
        return user.Role >= UserRole.Contributor ? "Contributor" : "Viewer";
    }
}

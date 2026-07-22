using SyncKit.Contract;

namespace EggIncognito.Services.RateLimiting;

public static class RateLimitKeys {

    internal const string NoCfKey = "no-cf";

    public static string ClientIp(HttpContext ctx, bool hosted) {
        var cf = ctx.Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();

        if (hosted) return NoCfKey;

        var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
        return !string.IsNullOrWhiteSpace(xff) ? xff.Split(',')[0].Trim() : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }



    public static string PartitionKey(HttpContext ctx, ICurrentUser user, bool hosted) =>
        user.IsAuthenticated && user.UserId is { } userId
            ? $"user:{userId}"
            : $"ip:{ClientIp(ctx, hosted)}";



    public static IReadOnlyList<string> TiersFor(ICurrentUser user) {
        if (!user.IsAuthenticated) return ["Anon"];
        var baseTier = user.IsAtLeast(UserRole.Contributor) ? "Contributor" : "Viewer";
        return user.IsSupporter ? [baseTier, "Supporter"] : [baseTier];
    }
}

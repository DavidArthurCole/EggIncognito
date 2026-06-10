using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;

namespace EggIncognito.Services.RateLimiting;

// Wires named rate-limit policies for ACTIVE actions only: egress (the Live-API send) and write
// (import/db/docs/admin). There is no global backstop and no read policy, so page loads, static
// assets, the SSE stream, and read APIs are never throttled. Each partition is keyed per caller (user
// id or client IP); the effective permit is min(policy, tier), so anonymous callers stay stricter.
// Rejections return 429 + Retry-After + a small JSON body.
public static class RateLimiterSetup
{
    public static IServiceCollection AddAppRateLimiter(this IServiceCollection services, IConfiguration config)
    {
        var opts = RateLimitOptions.Bind(config);
        if (!opts.Enabled)
        {
            services.AddRateLimiter(o => o.GlobalLimiter =
                PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetNoLimiter("disabled")));
            return services;
        }

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // No GlobalLimiter on purpose: only the explicitly-annotated action endpoints are limited.
            AddPolicy(o, "egress", opts);
            AddPolicy(o, "write", opts);

            o.OnRejected = async (ctx, ct) =>
            {
                var retry = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
                    ? (int)ra.TotalSeconds : 60;
                ctx.HttpContext.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "rate_limited", retryAfterSeconds = retry }, ct);
            };
        });
        return services;
    }

    private static void AddPolicy(RateLimiterOptions o, string policyKey, RateLimitOptions opts)
    {
        var optionKey = char.ToUpperInvariant(policyKey[0]) + policyKey[1..];
        o.AddPolicy(policyKey, ctx => Partition(ctx, optionKey, opts));
    }

    // Build a sliding-window partition for a caller, using the smaller of the policy + tier limits.
    private static RateLimitPartition<string> Partition(HttpContext ctx, string policyOptionKey, RateLimitOptions opts)
    {
        var user = ctx.RequestServices.GetRequiredService<ICurrentUser>();
        var key = RateLimitKeys.PartitionKey(ctx, user);
        var tier = opts.Tiers[RateLimitKeys.TierFor(user)];
        var policy = opts.Policies[policyOptionKey];
        var permit = Math.Min(policy.PermitLimit, tier.PermitLimit);

        return RateLimitPartition.GetSlidingWindowLimiter($"{policyOptionKey}:{key}", _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                SegmentsPerWindow = policy.SegmentsPerWindow,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
    }
}

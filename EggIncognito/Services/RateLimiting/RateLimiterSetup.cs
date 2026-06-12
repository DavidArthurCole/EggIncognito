using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;

namespace EggIncognito.Services.RateLimiting;

// Wires named rate-limit policies for explicitly-annotated endpoints only: egress (the Live-API
// send), write (import/db/docs/admin), and read (the public /api drop-in surface). There is no
// global backstop, so page loads, static assets, the SSE stream, and unannotated read APIs are
// never throttled. Each partition is keyed per caller by user id or client IP; the effective
// permit is min(policy, tier), so anonymous callers stay stricter. Rejections return 429 +
// Retry-After + a small JSON body.
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
            AddPolicy(o, "read", opts);

            o.OnRejected = async (ctx, ct) =>
            {
                // Sliding-window limiters with QueueLimit=0 never populate RetryAfter metadata, so
                // fall back to the matched policy's window instead of a hardcoded constant.
                var retry = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
                    ? (int)ra.TotalSeconds
                    : FallbackRetryAfterSeconds(ctx.HttpContext, opts);
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

    // The Retry-After fallback: the window of the policy named on the rejected endpoint, else 60s.
    internal static int FallbackRetryAfterSeconds(HttpContext ctx, RateLimitOptions opts)
    {
        var policyName = ctx.GetEndpoint()?.Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policyName is { Length: > 0 })
        {
            var optionKey = char.ToUpperInvariant(policyName[0]) + policyName[1..];
            if (opts.Policies.TryGetValue(optionKey, out var policy)) return policy.WindowSeconds;
        }
        return 60;
    }

    // The effective permit for a caller = the smaller of the policy and tier limits.
    internal static int EffectivePermit(RateLimitOptions opts, string tierName, string policyOptionKey) =>
        Math.Min(opts.Policies[policyOptionKey].PermitLimit, opts.Tiers[tierName].PermitLimit);

    // Build a sliding-window partition for a caller, using the smaller of the policy + tier limits.
    private static RateLimitPartition<string> Partition(HttpContext ctx, string policyOptionKey, RateLimitOptions opts)
    {
        var user = ctx.RequestServices.GetRequiredService<ICurrentUser>();
        var hosted = ctx.RequestServices.GetRequiredService<IAppMode>().Mode == AppMode.Hosted;
        var key = RateLimitKeys.PartitionKey(ctx, user, hosted);
        var policy = opts.Policies[policyOptionKey];
        var permit = EffectivePermit(opts, RateLimitKeys.TierFor(user), policyOptionKey);

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

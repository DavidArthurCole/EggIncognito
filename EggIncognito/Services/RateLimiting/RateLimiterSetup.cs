using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.RateLimiting;

// Wires named rate-limit policies for explicitly-annotated endpoints only: egress (the Live-API
// send), write (import/db/docs/admin), and read (the public /api drop-in surface). No global
// backstop, so page loads, static assets, and unannotated read APIs are never throttled. Rejections
// return 429 + Retry-After + a small JSON body.
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

            AddPolicy(o, "egress", opts);
            AddPolicy(o, "write", opts);
            AddPolicy(o, "read", opts);
            // "fetch" skips the per-tier min so anon visitors get the full Fetch limit, not Anon's 30/min.
            o.AddPolicy("fetch", ctx => Partition(ctx, "Fetch", opts, tierCapped: false));

            o.OnRejected = async (ctx, ct) =>
            {
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

    internal static bool IsExempt(ICurrentUser user) => user.IsAtLeast(UserRole.Admin);

    internal static int EffectivePermit(RateLimitOptions opts, IReadOnlyList<string> tierNames, string policyOptionKey)
    {
        var best = tierNames.Max(t => opts.Tiers[t].PermitLimit);
        return Math.Min(opts.Policies[policyOptionKey].PermitLimit, best);
    }

    // Build a sliding-window partition for a caller. tierCapped policies use min(policy, tier) so anon stays
    // stricter; tierCapped=false uses the flat policy limit.
    private static RateLimitPartition<string> Partition(
        HttpContext ctx, string policyOptionKey, RateLimitOptions opts, bool tierCapped = true)
    {
        var user = ctx.RequestServices.GetRequiredService<ICurrentUser>();
        if (IsExempt(user)) return RateLimitPartition.GetNoLimiter($"admin:{user.DiscordId}");
        var hosted = ctx.RequestServices.GetRequiredService<IAppMode>().Mode == AppMode.Hosted;
        var key = RateLimitKeys.PartitionKey(ctx, user, hosted);
        var policy = opts.Policies[policyOptionKey];
        var permit = tierCapped
            ? EffectivePermit(opts, RateLimitKeys.TiersFor(user), policyOptionKey)
            : policy.PermitLimit;

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

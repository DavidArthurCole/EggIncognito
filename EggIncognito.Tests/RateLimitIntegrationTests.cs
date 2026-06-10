using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EggIncognito.Tests;

// Proves the limiter blocks active actions: override the Write policy + Anon tier to a tiny permit
// count, fire more than that many POSTs at a write endpoint from one anonymous partition, and assert
// the overflow gets 429 + a Retry-After header. The limiter runs before the action, so the action's
// own 403/503 does not matter - only that the request is counted. Config override keeps the production
// defaults generous so the rest of the suite is never throttled. Read endpoints + page loads have no
// limiter, so they are not tested here.
public class RateLimitIntegrationTests : IClassFixture<RateLimitIntegrationTests.TinyLimitFactory>
{
    private readonly TinyLimitFactory _f;
    public RateLimitIntegrationTests(TinyLimitFactory f) => _f = f;

    [Fact]
    public async Task WriteEndpoint_Returns429_AfterLimit()
    {
        var c = _f.CreateClient();
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 10; i++)
        {
            var r = await c.PostAsJsonAsync("/api/db/route", new { path = "ei/x", response = "Y" });
            if (r.StatusCode == HttpStatusCode.TooManyRequests) { rejected = r; break; }
        }
        Assert.NotNull(rejected);
        Assert.True(rejected!.Headers.Contains("Retry-After"));
    }

    public sealed class TinyLimitFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NoBrowser"] = "true",
                ["RateLimiting:Policies:Write:PermitLimit"] = "3",
                ["RateLimiting:Policies:Write:WindowSeconds"] = "60",
                ["RateLimiting:Tiers:Anon:PermitLimit"] = "3",
            }));
            return base.CreateHost(builder);
        }
    }
}

using System.Text;
using EggIdentity.Client;
using EggIncognito.Data.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/feed")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class AdminFeedController(IServiceProvider services, IHttpClientFactory httpFactory)
    : ControllerBase {
    private FeedSubscriptionStore? Store =>
        services.GetService(typeof(FeedSubscriptionStore)) as FeedSubscriptionStore;

    private IdentityApiClient? Identity =>
        services.GetService(typeof(IdentityApiClient)) as IdentityApiClient;

    [HttpGet("subscriptions")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Subscriptions(CancellationToken ct) {
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        var subs = await Store.AllForAdminAsync(ct);
        var usernames = new Dictionary<Guid, string>();
        if (Identity is { } identity)
            foreach (var u in await identity.ListAdminUsersAsync(ct)) usernames[u.UserId] = u.Username;
        return Ok(FeedAdminGrouping.Build(subs, usernames));
    }

    [HttpPost("subscriptions/{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct) {
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        if (!await Store.AdminDeactivateAsync(id, ct)) return NotFound(new { error = "subscription not found" });
        FeedSubscriptionNotify.Changed(services);
        return Ok(new { deactivated = true });
    }

    [HttpDelete("subscriptions/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) {
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        if (!await Store.AdminDeleteAsync(id, ct)) return NotFound(new { error = "subscription not found" });
        FeedSubscriptionNotify.Changed(services);
        return Ok(new { deleted = true });
    }

    [HttpPost("subscriptions/{id:int}/test")]
    public async Task<IActionResult> Test(int id, [FromQuery] string? sample, CancellationToken ct) {
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        var sub = await Store.AdminByIdAsync(id, ct);
        if (sub is null) return NotFound(new { error = "subscription not found" });

        string kind = FeedEventKinds.Normalize(sub.EventKind);
        var fallback = FeedSamples.For(kind);
        var chosen = FeedSamples.Find(kind, sample) ?? (fallback.Count > 0 ? fallback[0] : null);
        string body = chosen is null
            ? """{"content":"EggIncognito feed test."}"""
            : DiscordFeedPayload.MarkAsTest(chosen.Event.BuildBody(sub.MessageTemplate));

        var http = httpFactory.CreateClient("discord-api");
        var res = await http.PostAsync(sub.TargetUrl,
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        if (!res.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });
        return Ok(new { tested = true, sample = chosen?.Key });
    }
}

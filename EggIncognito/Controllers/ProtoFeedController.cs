using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos/feed")]
public sealed class ProtoFeedController(IServiceProvider services, IHttpClientFactory httpFactory)
    : ControllerBase
{
    public sealed record CreateReq(string WebhookUrl, string[]? Platforms, string? Trigger, string? Label,
        string? MessageTemplate);

    private FeedSubscriptionStore? Store =>
        services.GetService(typeof(FeedSubscriptionStore)) as FeedSubscriptionStore;

    [HttpPost]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Create([FromBody] CreateReq req, CancellationToken ct)
    {
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        if (string.IsNullOrWhiteSpace(req.WebhookUrl) ||
            !req.WebhookUrl.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "a Discord webhook URL is required" });

        // Validate by sending a tiny test message; a bad/expired webhook 404s.
        var http = httpFactory.CreateClient("discord-api");
        var test = await http.PostAsync(req.WebhookUrl,
            new StringContent("""{"content":"EggIncognito proto feed connected."}""",
                System.Text.Encoding.UTF8, "application/json"), ct);
        if (!test.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });

        var sub = await Store.AddAsync(new FeedSubscription
        {
            Kind = "discord",
            TargetUrl = req.WebhookUrl,
            Platforms = req.Platforms is { Length: > 0 } ? req.Platforms : ["android", "ios"],
            Trigger = req.Trigger == "new_version" ? "new_version" : "proto_changed",
            Label = req.Label,
            MessageTemplate = string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate,
            OwnerUserId = (services.GetService(typeof(EggIncognito.Services.ICurrentUser))
                as EggIncognito.Services.ICurrentUser)?.UserId,
        }, ct);
        return Ok(new { sub.Id, sub.Platforms, sub.Trigger });
    }

    private Guid? OwnerUserId =>
        (services.GetService(typeof(EggIncognito.Services.ICurrentUser))
            as EggIncognito.Services.ICurrentUser)?.UserId;

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var subs = await Store.ByOwnerAsync(owner.Value, ct);
        return Ok(subs.Select(s => new
        {
            s.Id,
            s.Label,
            s.Platforms,
            s.Trigger,
            s.Active,
            s.CreatedAt,
            s.LastDeliveryAt,
            s.FailCount,
            s.MessageTemplate,
            UrlMasked = MaskWebhook(s.TargetUrl),
        }));
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var ok = await Store.DeleteAsync(id, owner.Value, ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        return Ok(new { deleted = true });
    }

    // Re-send a test message to an existing subscription's webhook (owner-gated). The client never holds the
    // full URL (it is masked), so the test must run server-side from the stored target.
    [HttpPost("{id:int}/test")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Test(int id, CancellationToken ct)
    {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var sub = (await Store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });

        // No real proto event exists for a manual test, so render the subscriber's own template (if any)
        // against sample data - this is what a real dispatch will actually send, not a generic placeholder.
        var body = string.IsNullOrWhiteSpace(sub.MessageTemplate)
            ? """{"content":"EggIncognito proto feed test."}"""
            : EggIncognito.Services.Feed.DiscordFeedPayload.Build(
                "android", "1.0.0", "1", "1", "0000000000000000000000000000000000000000",
                true, EggIncognito.Services.Feed.FeedDispatcher.BuildPageUrl(null, "android", "1"),
                sub.MessageTemplate);

        var http = httpFactory.CreateClient("discord-api");
        var res = await http.PostAsync(sub.TargetUrl,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        if (!res.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });
        return Ok(new { tested = true });
    }

    public sealed record UpdateReq(string[]? Platforms, string? Trigger, bool? Active, string? MessageTemplate);

    // Owner-gated edit of a subscription's platforms / trigger / active state (not the webhook URL). Mirrors
    // Delete's owner scoping. 404 when the subscription is not the caller's.
    [HttpPatch("{id:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReq req, CancellationToken ct)
    {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var ok = await Store.UpdateAsync(
            id, owner.Value,
            req.Platforms ?? ["android", "ios"],
            req.Trigger ?? "proto_changed",
            req.Active ?? true,
            req.MessageTemplate, ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        return Ok(new { updated = true });
    }

    // Identifies a webhook without leaking its token. Discord URL = /webhooks/{id}/{token}; id is public,
    // token is the secret, so show only its last 4 chars.
    public static string MaskWebhook(string url)
    {
        var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.IndexOf(parts, "webhooks");
        if (i >= 0 && i + 2 < parts.Length)
        {
            var webhookId = parts[i + 1];
            var token = parts[i + 2];
            var last4 = token.Length <= 4 ? token : token[^4..];
            return $"webhooks/{webhookId}/...{last4}";
        }
        var tail = url.Length <= 6 ? url : url[^6..];
        return $"...{tail}";
    }
}

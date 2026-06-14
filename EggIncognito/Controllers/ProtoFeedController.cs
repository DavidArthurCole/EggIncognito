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
    public sealed record CreateReq(string WebhookUrl, string[]? Platforms, string? Trigger, string? Label);

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
            OwnerUserId = (services.GetService(typeof(EggIncognito.Services.ICurrentUser))
                as EggIncognito.Services.ICurrentUser)?.DiscordId,
        }, ct);
        return Ok(new { sub.Id, sub.Platforms, sub.Trigger });
    }

    private string? DiscordId =>
        (services.GetService(typeof(EggIncognito.Services.ICurrentUser))
            as EggIncognito.Services.ICurrentUser)?.DiscordId;

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var owner = DiscordId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var subs = await Store.ByOwnerAsync(owner, ct);
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
            UrlMasked = MaskWebhook(s.TargetUrl),
        }));
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var owner = DiscordId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var ok = await Store.DeleteAsync(id, owner, ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        return Ok(new { deleted = true });
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

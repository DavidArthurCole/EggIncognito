using System.Text;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos/feed")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ProtoFeedController(IServiceProvider services, IHttpClientFactory httpFactory)
    : ControllerBase {
    private FeedSubscriptionStore? Store =>
        services.GetService(typeof(FeedSubscriptionStore)) as FeedSubscriptionStore;

    private Guid? OwnerUserId =>
        (services.GetService(typeof(ICurrentUser))
            as ICurrentUser)?.UserId;

    [HttpGet("kinds")]
    public IActionResult Kinds() => Ok(FeedEventKinds.All);

    [HttpPost]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Create([FromBody] CreateReq req, CancellationToken ct) {
        if (Store is null) return StatusCode(503, new { error = "no database configured" });
        if (string.IsNullOrWhiteSpace(req.WebhookUrl) ||
            !req.WebhookUrl.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)) {
            return BadRequest(new { error = "a Discord webhook URL is required" });
        }

        var http = httpFactory.CreateClient("discord-api");
        var test = await http.PostAsync(req.WebhookUrl,
            new StringContent("""{"content":"EggIncognito proto feed connected."}""",
                Encoding.UTF8, "application/json"), ct);
        if (!test.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });

        string kind = FeedEventKinds.Normalize(req.EventKind);
        var sub = await Store.AddAsync(new FeedSubscription {
            Kind = "discord",
            EventKind = kind,
            TargetUrl = req.WebhookUrl,
            Platforms = req.Platforms is { Length: > 0 } ? req.Platforms : ["android", "ios"],
            Trigger = FeedEventKinds.NormalizeTrigger(kind, req.Trigger),
            Label = req.Label,
            MessageTemplate = string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate,
            OwnerUserId = (services.GetService(typeof(ICurrentUser))
                as ICurrentUser)?.UserId
        }, ct);
        return Ok(new { sub.Id, sub.EventKind, sub.Platforms, sub.Trigger });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var subs = await Store.ByOwnerAsync(owner.Value, ct);
        return Ok(subs.Select(s => new {
            s.Id,
            s.Label,
            s.EventKind,
            s.Platforms,
            s.Trigger,
            s.Active,
            s.CreatedAt,
            s.LastDeliveryAt,
            s.FailCount,
            s.MessageTemplate,
            UrlMasked = MaskWebhook(s.TargetUrl)
        }));
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        bool ok = await Store.DeleteAsync(id, owner.Value, ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        return Ok(new { deleted = true });
    }


    [HttpPost("{id:int}/test")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Test(int id, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var sub = (await Store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });


        string body = sub.EventKind == FeedEventKinds.PeriodicalsChanged
            ? DiscordFeedPayload.BuildPeriodicals(
                "periodicals", "0000000000000000000000000000000000000000000000000000000000000000",
                $"{FeedDispatcher.DefaultPageBaseUrl}/periodicals", sub.MessageTemplate)
            : string.IsNullOrWhiteSpace(sub.MessageTemplate)
                ? """{"content":"EggIncognito feed test."}"""
                : DiscordFeedPayload.Build(
                    "android", "1.0.0", "1", "1", "0000000000000000000000000000000000000000",
                    true, FeedDispatcher.BuildPageUrl(null, "android", "1"), sub.MessageTemplate);

        var http = httpFactory.CreateClient("discord-api");
        var res = await http.PostAsync(sub.TargetUrl,
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        if (!res.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });
        return Ok(new { tested = true });
    }


    [HttpPatch("{id:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReq req, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        bool ok = await Store.UpdateAsync(
            id, owner.Value,
            req.Platforms ?? ["android", "ios"],
            req.Trigger ?? "proto_changed",
            req.Active ?? true,
            req.MessageTemplate, ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        return Ok(new { updated = true });
    }


    public static string MaskWebhook(string url) {
        string[] parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int i = Array.IndexOf(parts, "webhooks");
        if (i >= 0 && i + 2 < parts.Length) {
            string webhookId = parts[i + 1];
            string token = parts[i + 2];
            string last4 = token.Length <= 4 ? token : token[^4..];
            return $"webhooks/{webhookId}/...{last4}";
        }

        string tail = url.Length <= 6 ? url : url[^6..];
        return $"...{tail}";
    }

    public sealed record CreateReq(
        string WebhookUrl,
        string[]? Platforms,
        string? Trigger,
        string? Label,
        string? MessageTemplate,
        string? EventKind = null);

    public sealed record UpdateReq(string[]? Platforms, string? Trigger, bool? Active, string? MessageTemplate);
}

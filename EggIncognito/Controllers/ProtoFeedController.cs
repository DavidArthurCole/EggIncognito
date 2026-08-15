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
            !Uri.TryCreate(req.WebhookUrl, UriKind.Absolute, out var webhook) ||
            webhook.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(webhook.Host, "discord.com", StringComparison.OrdinalIgnoreCase) ||
            !webhook.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal)) {
            return BadRequest(new { error = "a Discord webhook URL is required" });
        }

        var http = httpFactory.CreateClient("discord-api");
        var test = await http.PostAsync(webhook,
            new StringContent("""{"content":"EggIncognito proto feed connected."}""",
                Encoding.UTF8, "application/json"), ct);
        if (!test.IsSuccessStatusCode)
            return BadRequest(new { error = "webhook rejected the test message" });

        string kind = FeedEventKinds.Normalize(req.EventKind);
        var sub = await Store.AddAsync(new FeedSubscription {
            Kind = "discord",
            EventKind = kind,
            TargetUrl = webhook.ToString(),
            Platforms = req.Platforms is { Length: > 0 } ? req.Platforms : ["android", "ios"],
            Trigger = FeedEventKinds.NormalizeTrigger(kind, req.Trigger),
            Filters = FeedEventKinds.NormalizeFilters(kind, req.Filters),
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
            s.Filters,
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
    public async Task<IActionResult> Test(int id, [FromQuery] string? sample, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var sub = (await Store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });

        var fallback = FeedSamples.For(sub.EventKind);
        var chosen = FeedSamples.Find(sub.EventKind, sample) ?? (fallback.Count > 0 ? fallback[0] : null);
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


    [HttpPost("preview")]
    [EnableRateLimiting("read")]
    public IActionResult Preview([FromBody] PreviewReq req) {
        string kind = FeedEventKinds.Normalize(req.EventKind);
        var probe = new FeedSubscription {
            EventKind = kind,
            Platforms = req.Platforms is { Length: > 0 } ? req.Platforms : ["android", "ios"],
            Trigger = FeedEventKinds.NormalizeTrigger(kind, req.Trigger),
            Filters = FeedEventKinds.NormalizeFilters(kind, req.Filters),
            MessageTemplate = string.IsNullOrWhiteSpace(req.MessageTemplate) ? null : req.MessageTemplate
        };

        return Ok(FeedSamples.For(kind).Select(s => {
            bool matches = s.Event.Matches(probe);
            var blocked = matches ? s.Event.BlockedBy(probe) : [];
            return new PreviewRow(
                s.Key, s.Label, s.Event.Summary, matches, blocked,
                matches && blocked.Count == 0 ? s.Event.BuildBody(probe.MessageTemplate) : null);
        }));
    }


    [HttpPatch("{id:int}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReq req, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is null) return StatusCode(503, new { error = "no database configured" });

        var sub = (await Store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });

        string trigger = FeedEventKinds.NormalizeTrigger(sub.EventKind, req.Trigger ?? sub.Trigger);
        bool ok = await Store.UpdateAsync(
            id, owner.Value,
            req.Platforms ?? ["android", "ios"],
            trigger,
            req.Active ?? true,
            req.MessageTemplate,
            ResolveFilters(sub, req.Filters), ct);
        if (!ok) return NotFound(new { error = "subscription not found" });
        return Ok(new { updated = true });
    }


    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> Activity(int id, CancellationToken ct) {
        var owner = OwnerUserId;
        if (owner is null) return Unauthorized(new { error = "log in to manage subscriptions" });
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });

        var sub = (await store.ByOwnerAsync(owner.Value, ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null) return NotFound(new { error = "subscription not found" });

        var deliveries = await store.DeliveriesAsync(id, ActivityTake, ct);
        var suppressions = await store.SuppressionsAsync(id, ActivityTake, ct);

        var rows = deliveries
            .Select(d => new ActivityRow(d.AttemptedAt, d.Status,
                string.IsNullOrEmpty(d.Summary) ? d.DedupKey : d.Summary, d.ResponseCode, null))
            .Concat(suppressions.Select(s => new ActivityRow(s.CreatedAt, "blocked",
                string.IsNullOrEmpty(s.Summary) ? s.DedupKey : s.Summary, null, s.Reason)))
            .OrderByDescending(r => r.At)
            .Take(ActivityTake);
        return Ok(rows);
    }

    private const int ActivityTake = 25;

    public sealed record ActivityRow(
        DateTimeOffset At, string Status, string Event, int? ResponseCode, string? Reason);

    public sealed record PreviewRow(
        string Key, string Label, string Event, bool Matches, IReadOnlyList<string> BlockedBy, string? Body);

    public sealed record PreviewReq(
        string? EventKind, string? Trigger, string[]? Platforms, string[]? Filters, string? MessageTemplate);


    public static string[] ResolveFilters(FeedSubscription sub, string[]? requested) =>
        FeedEventKinds.NormalizeFilters(sub.EventKind, requested ?? sub.Filters);

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
        string? EventKind = null,
        string[]? Filters = null);

    public sealed record UpdateReq(
        string[]? Platforms, string? Trigger, bool? Active, string? MessageTemplate, string[]? Filters = null);
}

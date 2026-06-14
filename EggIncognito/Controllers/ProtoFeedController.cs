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
}

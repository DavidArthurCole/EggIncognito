using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EggIncognito.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Controllers;

// Inbound device-farm sync endpoint. The farm POSTs a NewVersionEvent when it detects a new Egg Inc
// build; the handler ingests, classifies by protoSha, and stages a regen or flags a proto refresh.
// Opt-in via SyncEvent:EventSecret (404 when unset), bearer-authed against that secret. Mirrors
// synckit's NewVersionHandler semantics. Not a routes.yaml controller, like ImportController/ToolsController.
[ApiController]
[Route("events")]
[EnableRateLimiting("write")]
public sealed class EventsController(IConfiguration config, IServiceProvider services)
    : ControllerBase
{
    [HttpPost("new-version")]
    public async Task<IActionResult> NewVersion()
    {
        var secret = config["SyncEvent:EventSecret"];
        // No secret configured: the endpoint is conceptually unmounted, like the opt-in bot/DB paths.
        if (string.IsNullOrEmpty(secret)) return NotFound();

        if (!BearerMatches(secret)) return Unauthorized();

        NewVersionEvent? evt;
        try
        {
            evt = await JsonSerializer.DeserializeAsync<NewVersionEvent>(Request.Body);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "malformed event body" });
        }
        if (evt is null) return BadRequest(new { error = "empty event body" });

        var ingest = services.GetService(typeof(NewVersionIngestService)) as NewVersionIngestService;
        if (ingest is null)
            return StatusCode(503, new { error = "sync ingest not available" });

        var outcome = await ingest.HandleAsync(evt, HttpContext.RequestAborted);
        return Accepted(new { outcome = outcome.ToString() });
    }

    // Constant-time compare of the Authorization: Bearer <secret> header against the configured secret.
    private bool BearerMatches(string secret)
    {
        string? header = Request.Headers.Authorization;
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

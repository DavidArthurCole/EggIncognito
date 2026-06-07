using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;
using EggIncognito.Tooling.Capture;
using EggIncognito.Tooling.Dashboard;

namespace EggIncognito.Tooling.Controllers;

// Backend for the live capture dashboard SPA. Routes under /api/capture so they never collide
// with anything else. Thin: every action delegates to the shared CaptureHub / HarWriter /
// EndpointExtractor singletons that the capture loop also uses.
[ApiController]
[Route("api/capture")]
public sealed class CaptureDashboardController(
    CaptureHub hub,
    HarWriter har,
    EndpointExtractor extractor,
    FlowDecoder decoder) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Server-Sent Events: replay the snapshot, then stream each new flow as it is captured.
    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Flush the headers + an SSE comment immediately so the client sees the stream is open
        // even before any flow arrives (otherwise the response can sit buffered with zero flows).
        await Response.WriteAsync(":ok\n\n", ct);
        await Response.Body.FlushAsync(ct);

        // Subscribe BEFORE replaying the snapshot so no message is missed in the gap.
        var (reader, subscription) = hub.Subscribe();
        using (subscription)
        {
            // Replay current flows, then push the current stats so a fresh page is fully synced.
            foreach (var f in hub.Snapshot())
                await WriteEvent("flow", f, ct);
            await WriteEvent("stats", hub.StatsSnapshot(), ct);

            try
            {
                await foreach (var env in reader.ReadAllAsync(ct))
                {
                    object? payload = env.Kind switch
                    {
                        "flow" => env.Flow,
                        "stats" => env.Stats,
                        "notice" => env.Event,
                        _ => null,
                    };
                    if (payload is not null) await WriteEvent(env.Kind, payload, ct);
                }
            }
            catch (OperationCanceledException) { /* client disconnected */ }
        }
    }

    private async Task WriteEvent(string eventName, object payload, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(payload, Json);
        await Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    [HttpGet("flows")]
    public IActionResult Flows() => Ok(hub.Snapshot());

    // The sensitive JSON field names (for the dashboard's blur mode) - the same set the Redactor
    // tokenizes on write, plus the EID-bearing fields.
    [HttpGet("sensitive-keys")]
    public IActionResult SensitiveKeys() => Ok(new
    {
        keys = EggIncognito.Services.Redactor.SensitiveFieldNames
            .Concat(["eiUserId", "userId"]).Distinct().ToArray(),
    });

    [HttpGet("stats")]
    public IActionResult Stats() => Ok(hub.StatsSnapshot());

    [HttpPost("pause")]
    public IActionResult Pause() { hub.Paused = true; return Ok(new { paused = true }); }

    [HttpPost("resume")]
    public IActionResult Resume() { hub.Paused = false; return Ok(new { paused = false }); }

    [HttpPost("clear")]
    public IActionResult Clear() { hub.Clear(); return Ok(new { cleared = true }); }

    public sealed record SaveEndpointRequest(long Id);

    // Write a buffered flow's response as a fixture. Explicit save => overwrite.
    [HttpPost("save-endpoint")]
    public IActionResult SaveEndpoint([FromBody] SaveEndpointRequest body)
    {
        var flow = hub.Find(body.Id);
        if (flow is null) return NotFound(new { error = $"flow {body.Id} not in buffer" });

        var url = $"https://www.auxbrain.com/{flow.Path}";
        // Explicit save: bypass the live-capture dedup and force-overwrite the existing fixture.
        var path = extractor.ForceWriteEndpoint(url, flow.Method, flow.Status, flow.RequestDataB64, flow.ResponseB64);
        extractor.Save();
        return path is null
            ? StatusCode(422, new { error = "request could not be decoded into a fixture" })
            : Ok(new { saved = path });
    }

    [HttpGet("har")]
    public IActionResult Har()
    {
        var bytes = Encoding.UTF8.GetBytes(har.ToHar());
        return File(bytes, "application/json", "capture-session.har");
    }

    // Used by the decoder helper indirectly; exposed for completeness/debugging.
    [HttpGet("decode")]
    public IActionResult Decode([FromQuery] string path, [FromQuery] string responseB64)
    {
        var r = decoder.DecodeResponse(path, responseB64);
        return Ok(new { responseJson = r.Json, responseType = r.Type, known = r.Known });
    }
}

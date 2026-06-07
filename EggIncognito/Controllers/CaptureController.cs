using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using EggIncognito.Capture;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Backend for the capture dashboard SPA + the runtime start/stop control. Everything delegates to
// the singleton CaptureSession (and its Hub). Routes under /api/capture.
[ApiController]
[Route("api/capture")]
public sealed class CaptureController(CaptureSession session) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.WriteAsync(":ok\n\n", ct);
        await Response.Body.FlushAsync(ct);

        var (reader, subscription) = session.Hub.Subscribe();
        using (subscription)
        {
            foreach (var f in session.Hub.Snapshot()) await WriteEvent("flow", f, ct);
            await WriteEvent("stats", session.Hub.StatsSnapshot(), ct);
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
    public IActionResult Flows() => Ok(session.Hub.Snapshot());

    [HttpGet("sensitive-keys")]
    public IActionResult SensitiveKeys() => Ok(new
    {
        keys = Redactor.SensitiveFieldNames.Concat(["eiUserId", "userId"]).Distinct().ToArray(),
    });

    [HttpGet("stats")]
    public IActionResult Stats() => Ok(session.Hub.StatsSnapshot());

    [HttpGet("status")]
    public IActionResult Status() => Ok(session.Status);

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct) => Ok(await session.StartAsync(ct));

    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        await session.StopAsync();
        return Ok(new { running = false });
    }

    [HttpPost("pause")]
    public IActionResult Pause() { session.Hub.Paused = true; return Ok(new { paused = true }); }

    [HttpPost("resume")]
    public IActionResult Resume() { session.Hub.Paused = false; return Ok(new { paused = false }); }

    [HttpPost("clear")]
    public IActionResult Clear() { session.Hub.Clear(); return Ok(new { cleared = true }); }

    public sealed record SaveEndpointRequest(long Id);

    [HttpPost("save-endpoint")]
    public IActionResult SaveEndpoint([FromBody] SaveEndpointRequest body)
    {
        var flow = session.Hub.Find(body.Id);
        if (flow is null) return NotFound(new { error = $"flow {body.Id} not in buffer" });
        var path = session.SaveEndpoint(flow.Path, flow.Method, flow.Status, flow.RequestDataB64, flow.ResponseB64);
        return path is null
            ? StatusCode(409, new { error = "capture not running or flow could not be decoded" })
            : Ok(new { saved = path });
    }

    [HttpGet("har")]
    public IActionResult Har()
    {
        var bytes = Encoding.UTF8.GetBytes(session.CurrentHar());
        return File(bytes, "application/json", "capture-session.har");
    }

    [HttpGet("decode")]
    public IActionResult Decode([FromQuery] string path, [FromQuery] string responseB64)
    {
        var r = session.Decode(path, responseB64);
        return Ok(new { responseJson = r.Json, responseType = r.Type, known = r.Known });
    }
}

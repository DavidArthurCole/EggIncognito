// EggIncognito/Controllers/LogsApiController.cs
//
// Serves the in-memory log ring buffer to the Inspector's Logs panel.
//   GET /api/inspector/logs?level=basic|advanced&since={seq}
// basic   = Information and above (time/level/message)
// advanced = Debug and above (adds category + exception)
// `since` is the last sequence number the client has seen, for incremental polling.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EggIncognito.Logging;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/inspector/logs")]
public sealed class LogsApiController(IInMemoryLogStore store) : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] string level = "basic", [FromQuery] long since = 0)
    {
        var advanced = string.Equals(level, "advanced", StringComparison.OrdinalIgnoreCase);
        var minLevel = advanced ? LogLevel.Debug : LogLevel.Information;

        var entries = store.Since(since, minLevel).Select(e => new
        {
            seq = e.Seq,
            time = e.Timestamp.ToString("HH:mm:ss.fff"),
            level = e.Level.ToString(),
            category = advanced ? e.Category : null,
            message = e.Message,
            exception = advanced ? e.Exception : null,
        });

        return Ok(entries);
    }
}

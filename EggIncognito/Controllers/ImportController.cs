using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Write tooling: import a HAR into the content root's Endpoints/, update the endpoint_status block.
// Local-only; returns 403 in hosted mode since a request must not mutate shared data.
[ApiController]
[Route("api/import")]
[EnableRateLimiting("write")]
public sealed class ImportController(IConfiguration config, IAppMode appMode) : ControllerBase
{
    private string Root => ContentRoot.Resolve(config["ContentRoot"]);

    [HttpPost("har")]
    public async Task<IActionResult> Har(IFormFile file, [FromQuery] bool overwrite = false)
    {
        if (!appMode.CanWrite) return StatusCode(403, new { error = "imports are disabled in hosted mode" });
        if (file is null || file.Length == 0) return BadRequest(new { error = "no file uploaded" });

        var tmp = Path.GetTempFileName();
        try
        {
            await using (var fs = System.IO.File.Create(tmp)) await file.CopyToAsync(fs);
            var eid = config["EGG_INC_EID"] ?? Environment.GetEnvironmentVariable("EGG_INC_EID");
            var extractor = EndpointExtractor.ForRepo(Root, eid, "EI0000000000000000", overwrite);
            extractor.RunFromHar(tmp);
            extractor.Save();
            var c = extractor.Counts;
            return Ok(new { wrote = c.Wrote, upd = c.Upd, diff = c.Diff, same = c.Same, loss = c.Loss, err = c.Err });
        }
        finally { try { System.IO.File.Delete(tmp); } catch { } }
    }

    [HttpPost("endpoint-status/update")]
    public IActionResult UpdateStatus()
    {
        if (!appMode.CanWrite) return StatusCode(403, new { error = "writes are disabled in hosted mode" });
        var yamlPath = Path.Combine(Root, "RouteMap", "routes.yaml");
        var defaultsDir = Path.Combine(Root, "Endpoints", "default");
        EndpointStatus.WriteStatusBlock(yamlPath, EndpointStatus.Classify(yamlPath, defaultsDir));
        return Ok(new { updated = true });
    }
}

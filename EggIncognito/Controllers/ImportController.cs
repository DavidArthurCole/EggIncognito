using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/import")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("write")]
public sealed class ImportController(
    IConfiguration config,
    IAppMode appMode,
    DataCatalog catalog,
    PeriodicalsChangeNotifier notifier,
    ILogger<ImportController> logger) : ControllerBase {
    private string Root => ContentRoot.Resolve(config["ContentRoot"]);


    [HttpPost("har")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public Task<IActionResult> Har(IFormFile file, [FromQuery] bool overwrite = false)
        => Ingest(file, overwrite, (e, p) => e.RunFromHar(p));

    [HttpPost("mitm")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public Task<IActionResult> Mitm(IFormFile file, [FromQuery] bool overwrite = false)
        => Ingest(file, overwrite, (e, p) => e.RunFromMitm(p));

    private async Task<IActionResult> Ingest(IFormFile file, bool overwrite, Action<EndpointExtractor, string> run) {
        if (!appMode.CanWrite) return StatusCode(403, new { error = "imports are disabled in hosted mode" });
        if (file is null || file.Length == 0) return BadRequest(new { error = "no file uploaded" });

        string tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            await using (var fs = System.IO.File.Create(tmp)) await file.CopyToAsync(fs);
            string? eid = config["EGG_INC_EID"] ?? Environment.GetEnvironmentVariable("EGG_INC_EID");
            var extractor = EndpointExtractor.ForRepo(Root, eid, "EI0000000000000000", overwrite);
            extractor.LiveRoutes = new HashSet<string>(catalog.FeedWireRoutes(), StringComparer.Ordinal);
            extractor.WriteObserver = notifier;
            run(extractor, tmp);
            extractor.Save();
            var c = extractor.Counts;
            return Ok(new { wrote = c.Wrote, upd = c.Upd, diff = c.Diff, same = c.Same, loss = c.Loss, err = c.Err });
        } finally {
            try {
                System.IO.File.Delete(tmp);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                logger.LogDebug(ex, "import: temp upload {Tmp} left behind", tmp);
            }
        }
    }

    [HttpPost("endpoint-status/update")]
    public IActionResult UpdateStatus() {
        if (!appMode.CanWrite) return StatusCode(403, new { error = "writes are disabled in hosted mode" });
        string yamlPath = Path.Combine(Root, "RouteMap", "routes.yaml");
        string defaultsDir = Path.Combine(Root, "Endpoints", "default");
        EndpointStatus.WriteStatusBlock(yamlPath, EndpointStatus.Classify(yamlPath, defaultsDir));
        return Ok(new { updated = true });
    }
}

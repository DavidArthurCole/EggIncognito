using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Locally hosts the game's per-platform *Config (the ei/get_config ConfigResponse + its DLCCatalog of
// shells). Reads are public; writes (ingest a captured config, refresh from live) are admin-gated. The
// stored config feeds the shell viewer + asset pipeline so EGI has an offline copy of the latest Android +
// iOS config. Stored as proto JSON (JsonFormatter), the project's only sanctioned proto<->JSON codec.
[ApiController]
[Route("api/config")]
public sealed class ConfigController(
    GameConfigStore store, ITransportPipeline pipeline, IHttpClientFactory httpFactory,
    IAppMode appMode, ICurrentUser currentUser, ILogger<ConfigController> logger) : ControllerBase
{
    private const string GetConfigUrl = "https://www.auxbrain.com/ei/get_config";

    // What platforms have a stored config + when. Public read.
    [HttpGet]
    public IActionResult List()
    {
        if (!store.Enabled) return Ok(new { enabled = false, configs = Array.Empty<object>() });
        var configs = store.List().Select(c => new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes });
        return Ok(new { enabled = true, configs });
    }

    // The stored ConfigResponse JSON for a platform. Public read; large (multi-MB) so streamed as a file.
    [HttpGet("{platform}")]
    public IActionResult Get(string platform)
    {
        var c = store.Get(platform);
        if (c is null) return NotFound(new { error = "no stored config for that platform" });
        return File(System.Text.Encoding.UTF8.GetBytes(c.Json), "application/json", $"{platform}-config.json");
    }

    public sealed record IngestRequest(string ConfigResponseBase64);

    // Ingest a captured ConfigResponse (raw or AuthenticatedMessage-wrapped, base64). The reliable source:
    // the live API gates the full DLCCatalog behind a real client context, so a capture is the surest way to
    // get a complete config. Admin-gated. Stores the decoded proto as JSON.
    [HttpPost("{platform}/ingest")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Ingest(string platform, [FromBody] IngestRequest body, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        Ei.ConfigResponse cfg;
        try
        {
            var bytes = ProtoFraming.FromBase64Loose(body.ConfigResponseBase64 ?? "");
            var inner = ProtoFraming.TryUnwrap(bytes) ?? bytes;
            cfg = Ei.ConfigResponse.Parser.ParseFrom(inner);
        }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = $"could not parse ConfigResponse: {ex.Message}" }); }

        return await StoreAsync(platform, cfg, ct);
    }

    public sealed record IngestJsonRequest(string Json);

    // Ingest an already-DECODED ConfigResponse JSON (what /api/inspector/send returns in its `json` field,
    // fully unwrapped + decompressed by the Inspector pipeline). This is the reliable Admin path: it reuses
    // the Inspector's decode rather than re-implementing unwrap here (raw get_config bytes are wrapped +
    // compressed in a way a naive parse misreads as an empty config). Admin-gated.
    [HttpPost("{platform}/ingest-json")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> IngestJson(string platform, [FromBody] IngestJsonRequest body, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        Ei.ConfigResponse cfg;
        try { cfg = Ei.ConfigResponse.Parser.ParseJson(body.Json ?? ""); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = $"could not parse ConfigResponse JSON: {ex.Message}" }); }
        return await StoreAsync(platform, cfg, ct);
    }

    // Refresh from the live API: server signs a get_config and stores the response. Best-effort; the live
    // config can be thin (no DLCCatalog) without a full client context, so ingest-from-capture is preferred.
    // Egress-gated like Inspector send. Needs a signing salt (body Salt, or EGG_INC_API_SALT).
    public sealed record RefreshRequest(string? Salt);

    [HttpPost("{platform}/refresh-live")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> RefreshLive(string platform, [FromBody] RefreshRequest? body, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var salt = string.IsNullOrEmpty(body?.Salt) ? null : body!.Salt;
        if (salt is null && !pipeline.CanSign)
            return StatusCode(503, new { error = "live refresh needs a signing salt; ingest a captured config instead" });

        var req = new Ei.ConfigRequest { Rinfo = new Ei.BasicRequestInfo { Platform = platform } };
        var built = salt is null ? pipeline.Build(req.ToByteArray(), wrap: true)
                                 : pipeline.Build(req.ToByteArray(), wrap: true, salt);

        string rawBody;
        try
        {
            var client = httpFactory.CreateClient("inspector");
            var content = new StringContent(built.FinalFormBody, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var resp = await client.PostAsync(GetConfigUrl, content, ct);
            rawBody = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode) return Ok(new { ok = false, diagnostics = $"get_config -> HTTP {(int)resp.StatusCode}" });
        }
        catch (Exception ex) { logger.LogWarning(ex, "config refresh-live failed"); return Ok(new { ok = false, diagnostics = ex.Message }); }

        Ei.ConfigResponse cfg;
        try
        {
            var raw = ProtoFraming.FromBase64Loose(rawBody);
            var inner = ProtoFraming.TryUnwrap(raw) ?? raw;
            cfg = Ei.ConfigResponse.Parser.ParseFrom(inner);
        }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = $"could not decode get_config: {ex.Message}" }); }

        return await StoreAsync(platform, cfg, ct);
    }

    // Decodes the ConfigResponse to JSON (the sanctioned proto codec), stores it, and reports what it carries.
    private async Task<IActionResult> StoreAsync(string platform, Ei.ConfigResponse cfg, CancellationToken ct)
    {
        if (!store.Enabled) return StatusCode(503, new { error = "config store needs ConfigStore:Dir or ShipAssets:OutputDir configured" });
        var json = JsonFormatter.Default.Format(cfg);
        await store.SaveAsync(platform, json, ct);
        var shells = cfg.DlcCatalog?.Shells.Count ?? 0;
        var shellObjects = cfg.DlcCatalog?.ShellObjects.Count ?? 0;
        logger.LogInformation("config stored: {Platform} ({Bytes}B, {Shells} shells)", platform, json.Length, shells);
        return Ok(new { ok = true, platform, bytes = json.Length, hasDlcCatalog = cfg.DlcCatalog is not null, shells, shellObjects });
    }

    private IActionResult? RequireAdmin() =>
        currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin)
            ? null : StatusCode(403, new { error = "admin role required" });
}

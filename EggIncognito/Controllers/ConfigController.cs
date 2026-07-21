using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;

namespace EggIncognito.Controllers;


[ApiController]
[Route("api/config")]
public sealed class ConfigController(
    GameConfigStore store, ITransportPipeline pipeline, IHttpClientFactory httpFactory,
    ICurrentUser currentUser, IAppMode appMode, IConfiguration config,
    EggIncognito.Services.Feed.PeriodicalsChangeNotifier notifier,
    ILogger<ConfigController> logger) : ControllerBase
{
    private const string GetConfigUrl = "https://www.auxbrain.com/ei/get_config";
    private const string GetPeriodicalsUrl = "https://www.auxbrain.com/ei/get_periodicals";

    [HttpGet]
    public IActionResult List()
    {
        if (!store.Enabled) return Ok(new { enabled = false, configs = Array.Empty<object>() });
        var configs = store.List().Select(c => new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes });
        return Ok(new { enabled = true, configs });
    }

   
    [HttpGet("{platform}")]
    public IActionResult Get(string platform)
    {
        var c = store.Get(platform);
        if (c is null) return NotFound(new { error = "no stored config for that platform" });
        return File(System.Text.Encoding.UTF8.GetBytes(c.Json), "application/json", $"{platform}-config.json");
    }

    public sealed record IngestRequest(string ConfigResponseBase64);

   
   
    [HttpPost("{platform}/ingest")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Ingest(string platform, [FromBody] IngestRequest body, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        byte[] bytes;
        try { bytes = ProtoFraming.FromBase64Loose(body.ConfigResponseBase64 ?? ""); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = $"not valid base64: {ex.Message}" }); }

       
       
        var cfg = BestConfig(bytes);
        if (cfg is null) return Ok(new { ok = false, diagnostics = "could not parse as a ConfigResponse (wrapped or direct)" });
        return await StoreAsync(platform, cfg, ct);
    }

   
    private Ei.ConfigResponse? BestConfig(byte[] bytes)
    {
        Ei.ConfigResponse? Try(byte[] b)
        {
            try { return Ei.ConfigResponse.Parser.ParseFrom(b); }
            catch { return null; }
        }
        int Shells(Ei.ConfigResponse? c) => c?.DlcCatalog?.Shells.Count ?? 0;

        var direct = Try(bytes);
        Ei.ConfigResponse? unwrapped = null;
        var inner = ProtoFraming.TryUnwrap(bytes);
        if (inner is not null) unwrapped = Try(inner);

        var best = Shells(unwrapped) > Shells(direct) ? unwrapped : direct;
        best ??= unwrapped ?? direct;
        logger.LogInformation("config ingest: direct={D} shells, unwrapped={U} shells -> chose {C}",
            Shells(direct), Shells(unwrapped), ReferenceEquals(best, unwrapped) ? "unwrapped" : "direct");
        return best;
    }

    public sealed record IngestJsonRequest(string Json);

   
   
    [HttpPost("{platform}/ingest-json")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> IngestJson(string platform, [FromBody] IngestJsonRequest body, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var jsonLen = body.Json?.Length ?? 0;
        logger.LogInformation("config ingest-json: {Platform} received {Len} chars", platform, jsonLen);
        Ei.ConfigResponse cfg;
        try { cfg = Ei.ConfigResponse.Parser.ParseJson(body.Json ?? ""); }
        catch (Exception ex)
        {
            logger.LogWarning("config ingest-json: {Platform} ParseJson failed: {Err}", platform, ex.Message);
            return Ok(new { ok = false, diagnostics = $"could not parse ConfigResponse JSON ({jsonLen} chars): {ex.Message}" });
        }
        logger.LogInformation("config ingest-json: {Platform} parsed, dlcCatalog={Has}, shells={Shells}",
            platform, cfg.DlcCatalog is not null, cfg.DlcCatalog?.Shells.Count ?? 0);
        return await StoreAsync(platform, cfg, ct);
    }

   
   
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

    public sealed record RefreshEndpointsRequest(string? Salt, string? Platform);

    [HttpPost("refresh-endpoints")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> RefreshEndpoints([FromBody] RefreshEndpointsRequest? body, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        if (!appMode.CanWrite) return StatusCode(403, new { error = "endpoint writes disabled in this mode" });
        var salt = string.IsNullOrEmpty(body?.Salt) ? null : body!.Salt;
        if (salt is null && !pipeline.CanSign)
            return StatusCode(503, new { error = "live refresh needs a signing salt; ingest a captured response instead" });
        var platform = string.IsNullOrEmpty(body?.Platform) ? "IOS" : body!.Platform;

        var contentRoot = ContentRoot.Resolve(config["ContentRoot"]);
        var extractor = EndpointExtractor.ForRepo(contentRoot, eid: null, "EI0000000000000000", overwrite: true);
        extractor.Quiet = true;
        extractor.WriteObserver = notifier;

        async Task<object> One(string url, string label, byte[] inner)
        {
            var (ok, raw, diag) = await FetchRawAsync(url, inner, salt, ct);
            if (!ok) return new { endpoint = label, ok = false, diagnostics = diag };
            string? written;
            try { written = extractor.ForceWriteEndpoint(url, "POST", 200, null, raw); }
            catch (Exception ex) { return new { endpoint = label, ok = false, diagnostics = $"write failed: {ex.Message}" }; }
            return new { endpoint = label, ok = written is not null, path = written };
        }

        var cfg = await One(GetConfigUrl, "ei/get_config",
            new Ei.ConfigRequest { Rinfo = new Ei.BasicRequestInfo { Platform = platform } }.ToByteArray());
        var per = await One(GetPeriodicalsUrl, "ei/get_periodicals",
            new Ei.GetPeriodicalsRequest { Rinfo = new Ei.BasicRequestInfo { Platform = platform } }.ToByteArray());

        return Ok(new { results = new[] { cfg, per } });
    }

    private async Task<(bool Ok, string Raw, string Diag)> FetchRawAsync(string url, byte[] inner, string? salt, CancellationToken ct)
    {
        var built = salt is null ? pipeline.Build(inner, wrap: true) : pipeline.Build(inner, wrap: true, salt);
        try
        {
            var client = httpFactory.CreateClient("inspector");
            var content = new StringContent(built.FinalFormBody, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var resp = await client.PostAsync(url, content, ct);
            var raw = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode) return (false, "", $"HTTP {(int)resp.StatusCode}");
            return (true, raw, "ok");
        }
        catch (Exception ex) { logger.LogWarning(ex, "live fetch {Url} failed", url); return (false, "", ex.Message); }
    }

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

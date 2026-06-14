using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Backfill;
using EggIncognito.Services.Backfill.Sources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EggIncognito.Controllers;

// Admin-only proto-backfill triggers + status. Each mirrors AdminController's gating: the role check
// runs before the DB resolve, so a non-admin 403s and a no-DB caller 503s regardless of state. The
// importers run in the background (each opens its own DI scope), so the request returns immediately.
//
// GET /api/protos/backfill/status response shape (STABLE; the UI agent depends on this):
//   {
//     "jobs":  [ { "source", "status", "startedAt", "finishedAt", "imported", "total", "note" } ],
//     "known": [ { "platform", "appVersion", "releaseDate", "changelog", "source" } ]
//   }
// jobs = latest run per source (newest first); known = the known_versions discovery list (newest first).
[ApiController]
[Route("api/protos/backfill")]
[EnableRateLimiting("write")]
public sealed class BackfillController(IServiceProvider services, ICurrentUser user) : ControllerBase
{
    private static readonly string[] ListSources = ["fandom", "uptodown", "apkpure", "itunes", "ipa4fun"];
    private const string NoDb = "backfill not available (no DB)";

    private IActionResult? RequireAdmin() =>
        user.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin only" });

    [HttpPost("elgranjero")]
    public IActionResult Elgranjero()
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(ElgranjeroImporter)) is not ElgranjeroImporter importer)
            return StatusCode(503, new { error = NoDb });
        var by = user.DiscordId;
        _ = Task.Run(() => importer.RunAsync(by, CancellationToken.None));
        return Accepted(new { status = "elgranjero import started" });
    }

    [HttpPost("playstore")]
    public IActionResult PlayStore()
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(PlayStoreImporter)) is not PlayStoreImporter importer)
            return StatusCode(503, new { error = NoDb });
        _ = Task.Run(() => importer.RunAsync(CancellationToken.None));
        return Accepted(new { status = "playstore import started" });
    }

    [HttpPost("appstore")]
    public IActionResult AppStore()
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(AppStoreImporter)) is not AppStoreImporter importer)
            return StatusCode(503, new { error = NoDb });
        _ = Task.Run(() => importer.RunAsync(CancellationToken.None));
        return Accepted(new { status = "appstore import started" });
    }

    // Runs one version-list adapter (fandom|uptodown|apkpure|itunes|ipa4fun) into known_versions.
    [HttpPost("list/{source}")]
    public IActionResult List(string source)
    {
        if (RequireAdmin() is { } no) return no;
        if (!ListSources.Contains(source))
            return StatusCode(400, new { error = "unknown source" });
        if (services.GetService(typeof(VersionListImporter)) is not VersionListImporter importer)
            return StatusCode(503, new { error = NoDb });
        // Adapters are keyed by name; resolve from the keyed registrations.
        if (services.GetKeyedService<IVersionListSource>(source) is not IVersionListSource adapter)
            return StatusCode(503, new { error = "source adapter not available" });
        var by = user.DiscordId;
        _ = Task.Run(() => importer.RunAsync(adapter, by, CancellationToken.None));
        return Accepted(new { status = $"{source} list import started" });
    }

    public sealed record ApkExtractRequest(string AppVersion);

    // The heavy per-APK extract; 501 when ProtoExtract is not configured on this host.
    [HttpPost("apk-extract")]
    public async Task<IActionResult> ApkExtract([FromBody] ApkExtractRequest req)
    {
        if (RequireAdmin() is { } no) return no;
        if (string.IsNullOrWhiteSpace(req?.AppVersion))
            return StatusCode(400, new { error = "appVersion required" });
        if (services.GetService(typeof(ApkExtractService)) is not ApkExtractService extract)
            return StatusCode(503, new { error = NoDb });
        if (!extract.Options.IsConfigured)
            return StatusCode(501, new { error = "extraction not configured on this host" });
        var version = req.AppVersion;
        _ = Task.Run(() => extract.ExtractAsync(version, CancellationToken.None));
        return Accepted(new { status = $"apk-extract started for {version}" });
    }

    // Deletes keyless/stub registry rows (empty build or appVersion). The admin UI fires this once on
    // load so a malformed row never lingers. No DB => 0 pruned.
    [HttpPost("prune")]
    public async Task<IActionResult> Prune(CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(ProtoRegistryStore)) is not ProtoRegistryStore store)
            return Ok(new { pruned = 0 });
        var pruned = await store.PruneEmptyAsync(ct);
        return Ok(new { pruned });
    }

    public sealed record ResyncRequest(string? Platform);

    // Forwards a force re-sync to the host-side runner agent (the device farm). Admin-gated. The agent URL
    // and secret are host config (RUNNER_AGENT_URL/RUNNER_AGENT_SECRET), mirroring the deploy-agent. Unset
    // (or no config/http in the no-DB unit-test path) returns 501 not-configured, like apk-extract, since
    // most hosts have no runner.
    [HttpPost("runner-resync")]
    public async Task<IActionResult> RunnerResync([FromBody] ResyncRequest? req, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var config = services.GetService(typeof(IConfiguration)) as IConfiguration;
        var httpFactory = services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var url = config?["RUNNER_AGENT_URL"];
        var secret = config?["RUNNER_AGENT_SECRET"];
        if (httpFactory is null || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
            return StatusCode(501, new { error = "runner agent not configured on this host" });

        try
        {
            var http = httpFactory.CreateClient();
            using var msg = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/resync");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            msg.Content = JsonContent.Create(new { force = true });
            var res = await http.SendAsync(msg, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            return StatusCode((int)res.StatusCode, new { runner = body });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = $"runner agent unreachable: {ex.Message}" });
        }
    }

    // Latest job per source + the known-versions discovery list. The admin UI polls this for live status.
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(IBackfillJobStore)) is not IBackfillJobStore jobs)
            return Ok(new { jobs = Array.Empty<object>(), known = Array.Empty<object>() });

        var jobRows = await jobs.LatestPerSourceAsync(ct);
        var known = await jobs.KnownAsync(ct);
        return Ok(new
        {
            jobs = jobRows.Select(j => new
            {
                source = j.Source,
                status = j.Status,
                startedAt = j.StartedAt,
                finishedAt = j.FinishedAt,
                imported = j.Imported,
                total = j.Total,
                note = j.Note,
            }),
            known = known.Select(k => new
            {
                platform = k.Platform,
                appVersion = k.AppVersion,
                releaseDate = k.ReleaseDate,
                changelog = k.Changelog,
                source = k.Source,
            }),
        });
    }
}

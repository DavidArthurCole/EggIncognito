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

//


[ApiController]
[Route("api/protos/backfill")]
[EnableRateLimiting("write")]
public sealed class BackfillController(IServiceProvider services, ICurrentUser user) : ControllerBase
{
    private static readonly string[] ListSources = ["fandom", "uptodown", "apkpure", "itunes", "ipa4fun", "archive"];
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

    [HttpPost("list/{source}")]
    public IActionResult List(string source)
    {
        if (RequireAdmin() is { } no) return no;
        if (!ListSources.Contains(source))
            return StatusCode(400, new { error = "unknown source" });
        if (services.GetService(typeof(VersionListImporter)) is not VersionListImporter importer)
            return StatusCode(503, new { error = NoDb });
        if (services.GetKeyedService<IVersionListSource>(source) is not IVersionListSource adapter)
            return StatusCode(503, new { error = "source adapter not available" });
        var by = user.DiscordId;
        _ = Task.Run(() => importer.RunAsync(adapter, by, CancellationToken.None));
        return Accepted(new { status = $"{source} list import started" });
    }

    public sealed record ApkExtractRequest(string AppVersion);

   
    [HttpPost("apk-extract")]
    public async Task<IActionResult> ApkExtract([FromBody] ApkExtractRequest req, CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        if (string.IsNullOrWhiteSpace(req?.AppVersion))
            return StatusCode(400, new { error = "appVersion required" });
        var version = req.AppVersion;

        var scopeFactory = services.GetService(typeof(IServiceScopeFactory)) as IServiceScopeFactory;

        if (services.GetService(typeof(ApkExtractService)) is ApkExtractService extract && extract.Options.IsConfigured)
        {
            await StartExtractJob(scopeFactory, version, ct);
            _ = Task.Run(async () =>
            {
                try
                {
                    await extract.ExtractAsync(version, CancellationToken.None);
                    await FinishExtractJob(scopeFactory, version, "done", null);
                }
                catch (Exception ex)
                {
                    await FinishExtractJob(scopeFactory, version, "failed", ex.Message);
                }
            });
            return Accepted(new { status = $"apk-extract started for {version}" });
        }

        var config = services.GetService(typeof(IConfiguration)) as IConfiguration;
        var httpFactory = services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var url = config?["RUNNER_AGENT_URL"];
        var secret = config?["RUNNER_AGENT_SECRET"];
        if (httpFactory is null || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
            return StatusCode(501, new { error = "extraction not configured on this host" });

        await StartExtractJob(scopeFactory, version, ct);
        try
        {
            var http = httpFactory.CreateClient();
            using var msg = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/extract");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            msg.Content = JsonContent.Create(new { appVersion = version });
            var res = await http.SendAsync(msg, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            await FinishExtractJob(scopeFactory, version,
                res.IsSuccessStatusCode ? "done" : "failed", Truncate(body));
            return StatusCode((int)res.StatusCode, new { runner = body });
        }
        catch (Exception ex)
        {
            await FinishExtractJob(scopeFactory, version, "failed", ex.Message);
            return StatusCode(502, new { error = $"runner agent unreachable: {ex.Message}" });
        }
    }

    private static string? Truncate(string? s) =>
        string.IsNullOrEmpty(s) ? null : (s.Length <= 500 ? s : s[..500]);

    private static async Task StartExtractJob(IServiceScopeFactory? scopeFactory, string version, CancellationToken ct)
    {
        if (scopeFactory is null) return;
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService<IBackfillJobStore>() is { } jobs)
            await jobs.StartExtractAsync("android", version, ct);
    }

    private static async Task FinishExtractJob(
        IServiceScopeFactory? scopeFactory, string version, string status, string? note)
    {
        if (scopeFactory is null) return;
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService<IBackfillJobStore>() is { } jobs)
            await jobs.FinishExtractAsync("android", version, status, note, CancellationToken.None);
    }

   
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

   
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(IBackfillJobStore)) is not IBackfillJobStore jobs)
            return Ok(new
            {
                jobs = Array.Empty<object>(),
                known = Array.Empty<object>(),
                extractJobs = Array.Empty<object>(),
            });

        var jobRows = await jobs.LatestPerSourceAsync(ct);
        var known = await jobs.KnownAsync(ct);
        var extractJobs = await jobs.ListExtractJobsAsync(ct);
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
            extractJobs = extractJobs.Select(e => new
            {
                platform = e.Platform,
                appVersion = e.AppVersion,
                status = e.Status,
                finishedAt = e.FinishedAt,
                note = e.Note,
            }),
        });
    }

   
   
    [HttpGet("known")]
    [EnableRateLimiting("fetch")]
    public async Task<IActionResult> Known(CancellationToken ct)
    {
        if (services.GetService(typeof(IBackfillJobStore)) is not IBackfillJobStore jobs)
            return Ok(Array.Empty<object>());
        var known = await jobs.KnownAsync(ct);
        return Ok(known.Select(k => new
        {
            platform = k.Platform,
            appVersion = k.AppVersion,
            releaseDate = k.ReleaseDate,
            changelog = k.Changelog,
            source = k.Source,
        }));
    }
}

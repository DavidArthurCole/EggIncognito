using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Controllers;

// Device status (public read) + admin refresh. Reads project the latest probe per device; refresh runs an
// immediate probe tagged admin:<id> through the same DeviceProbeRunner the background poller uses. DB-gated:
// reads return [] without Postgres, writes return 503. Admin gate mirrors AdminController.RequireAdmin.
[ApiController]
[Route("api/devices")]
[EnableRateLimiting("read")]
public sealed class DevicesController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase
{
    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IActionResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var store = Store;
        if (store is null) return Ok(Array.Empty<object>());
        var latest = await store.LatestPerDeviceAsync();
        var devices = (await store.EnabledDevicesAsync()).ToDictionary(d => d.Id);

        // The client_version for an installed build is not in dumpsys (it lives in the binary), but if that
        // build is already in the registry we can show its known client_version. Look up by (platform, build).
        var db = Db;
        var clientByKey = new Dictionary<(string, string), string?>();
        if (db is not null)
        {
            var builds = latest.Where(p => p.InstalledBuild is not null)
                .Select(p => (devices.TryGetValue(p.DeviceId, out var d) ? d.Platform : "", p.InstalledBuild!))
                .ToHashSet();
            foreach (var (plat, build) in builds)
            {
                var row = await db.ProtoVersions.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Platform == plat && v.Build == build && v.DeletedAt == null);
                clientByKey[(plat, build)] = row?.ClientVersion;
            }
        }

        var rows = latest.Where(p => devices.ContainsKey(p.DeviceId)).Select(p =>
        {
            var d = devices[p.DeviceId];
            clientByKey.TryGetValue((d.Platform, p.InstalledBuild ?? ""), out var client);
            return new
            {
                id = d.Id, platform = d.Platform, label = d.Label,
                reachable = p.Reachable,
                installedAppVersion = p.InstalledAppVersion,
                installedBuild = p.InstalledBuild,
                installedClientVersion = client,
                latestAvailable = p.LatestAvailable,
                result = p.Result,
                note = p.Note,
                probedAt = p.ProbedAt,
            };
        });
        return Ok(rows);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> History(string id, [FromQuery] int n = 20)
    {
        var store = Store;
        if (store is null) return Ok(Array.Empty<object>());
        var rows = await store.HistoryAsync(id, Math.Clamp(n, 1, 100));
        return Ok(rows.Select(p => new
        {
            probedAt = p.ProbedAt, reachable = p.Reachable,
            installedAppVersion = p.InstalledAppVersion, installedBuild = p.InstalledBuild,
            result = p.Result, triggeredBy = p.TriggeredBy, note = p.Note,
        }));
    }

    [HttpPost("{id}/refresh")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Refresh(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        var db = Db;
        if (store is null || db is null) return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });

        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var upgrader = (IDeviceUpgrader)services.GetRequiredService(typeof(IDeviceUpgrader));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));

        var row = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin:{currentUser.DiscordId}", runner, store, db, upgrader, logger, time, HttpContext.RequestAborted);

        return Ok(new
        {
            id = device.Id, platform = device.Platform, label = device.Label,
            reachable = row.Reachable, installedAppVersion = row.InstalledAppVersion,
            installedBuild = row.InstalledBuild, latestAvailable = row.LatestAvailable,
            result = row.Result, note = row.Note, probedAt = row.ProbedAt,
        });
    }

    // Pull the installed app off the device, carve its proto, and upsert a registry row. Android only:
    // it shells `adb pull` for the arm split, then reuses ApkExtractService (pbtk + versionCode + save).
    // iOS has no in-app extraction path yet (needs a decrypted-binary pull), so it returns 501. Admin +
    // DB gated; 501 when the extraction toolchain is not configured on this host.
    [HttpPost("{id}/save")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string id)
    {
        if (RequireAdmin() is { } no) return no;
        var store = Store;
        if (store is null || Db is null) return StatusCode(503, new { error = "no database configured" });

        var device = await store.GetAsync(id);
        if (device is null) return NotFound(new { error = "unknown device" });
        if (device.Platform != "android")
            return StatusCode(501, new { error = "device proto extraction is android-only for now" });

        // Read the installed version first so the registry row is labelled correctly.
        var runner = (IProcessRunner)services.GetRequiredService(typeof(IProcessRunner));
        var probe = await DeviceProbeRunner.ProbeFor(device, runner).ProbeAsync(HttpContext.RequestAborted);
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion))
            return StatusCode(502, new { error = "device unreachable or no version read" });

        var extract = (EggIncognito.Services.Backfill.ApkExtractService)
            services.GetRequiredService(typeof(EggIncognito.Services.Backfill.ApkExtractService));
        if (!extract.Options.IsConfigured)
            return StatusCode(501, new { error = "proto extraction not configured on this host" });

        var puller = new EggIncognito.Services.Devices.DeviceApkPuller(runner);
        var apk = await puller.PullArmSplitAsync(device.Target, device.Package, HttpContext.RequestAborted);
        if (apk is null)
            return StatusCode(502, new { error = "could not pull the arm split apk from the device" });

        try
        {
            await extract.ExtractFromArmSplitAsync(
                apk, probe.InstalledAppVersion!, $"device:{device.Id}", HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"extraction failed: {ex.Message}" });
        }

        // Re-probe so the card reflects the now-extracted build (result should flip to no_change).
        var db = Db!;
        var upgrader = (IDeviceUpgrader)services.GetRequiredService(typeof(IDeviceUpgrader));
        var time = (TimeProvider)services.GetRequiredService(typeof(TimeProvider));
        var logger = (ILogger<DevicesController>)services.GetRequiredService(typeof(ILogger<DevicesController>));
        var row = await DeviceProbeRunner.ProbeOneAsync(
            device, $"admin-save:{currentUser.DiscordId}", runner, store, db, upgrader, logger, time, HttpContext.RequestAborted);

        return Ok(new
        {
            saved = true, appVersion = probe.InstalledAppVersion, build = probe.InstalledBuild,
            result = row.Result,
        });
    }
}

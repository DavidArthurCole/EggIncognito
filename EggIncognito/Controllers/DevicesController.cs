using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
        var rows = latest.Where(p => devices.ContainsKey(p.DeviceId)).Select(p =>
        {
            var d = devices[p.DeviceId];
            return new
            {
                id = d.Id, platform = d.Platform, label = d.Label,
                reachable = p.Reachable,
                installedAppVersion = p.InstalledAppVersion,
                installedBuild = p.InstalledBuild,
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
}

using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Models.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/devices")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("read")]
public sealed class DeviceCookbooksController(
    ICurrentUser currentUser,
    IServiceProvider services) : ControllerBase {
    private DeviceCookbookRunner? Runner =>
        services.GetService(typeof(DeviceCookbookRunner)) as DeviceCookbookRunner;

    private DeviceTimelineCache? Timeline =>
        services.GetService(typeof(DeviceTimelineCache)) as DeviceTimelineCache;

    private DeviceCookbookFeed? Cookbooks =>
        services.GetService(typeof(DeviceCookbookFeed)) as DeviceCookbookFeed;

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("{id}/cookbooks")]
    public async Task<IActionResult> List(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner) return StatusCode(503, new { error = "no database configured" });

        if (await runner.TargetAsync(id, ct) is null) return NotFound(new { error = "unknown device" });
        IReadOnlyList<DeviceCookbookInfo> infos = await runner.DescribeAsync(id, ct);
        return Ok(infos);
    }

    [HttpPost("{id}/cookbooks")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Start(string id, [FromBody] DeviceCookbookRequest? request,
        CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (request is null || string.IsNullOrWhiteSpace(request.CookbookId))
            return BadRequest(new { error = "cookbookId required" });
        if (Runner is not { } runner) return StatusCode(503, new { error = "no database configured" });

        string who = currentUser.DiscordId ?? "?";
        var start = await runner.StartAsync(id, request, $"admin:{who}", ct);
        return start.Outcome switch {
            DeviceCookbookStartOutcome.Started =>
                Accepted(new { device = id, jobId = start.JobId, cookbook = request.CookbookId, state = "running" }),
            DeviceCookbookStartOutcome.UnknownDevice => NotFound(new { error = start.Error }),
            DeviceCookbookStartOutcome.UnknownCookbook => NotFound(new { error = start.Error }),
            DeviceCookbookStartOutcome.Unavailable => StatusCode(409, new { error = start.Error }),
            DeviceCookbookStartOutcome.Busy => StatusCode(409, new { error = start.Error }),
            _ => StatusCode(503, new { error = start.Error ?? "cookbooks are not configured" })
        };
    }

    [HttpGet("{id}/cookbooks/running")]
    public async Task<IActionResult> Running(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner) return StatusCode(503, new { error = "no database configured" });
        if (Timeline is not { } timeline) return StatusCode(503, new { error = "no database configured" });

        if (await runner.TargetAsync(id, ct) is null) return NotFound(new { error = "unknown device" });

        var latest = await timeline.LatestAsync(id, DeviceJobKinds.Cookbook, ct);
        bool running = latest is { State: DeviceJobStates.Running };
        return Ok(new { running, jobId = running ? latest!.Id : (long?)null });
    }

    [HttpPost("{id}/cookbooks/stop")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Stop(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Runner is not { } runner) return StatusCode(503, new { error = "no database configured" });
        if (Timeline is not { } timeline) return StatusCode(503, new { error = "no database configured" });

        if (await runner.TargetAsync(id, ct) is null) return NotFound(new { error = "unknown device" });

        var latest = await timeline.LatestAsync(id, DeviceJobKinds.Cookbook, ct);
        if (latest is not { State: DeviceJobStates.Running })
            return StatusCode(409, new { error = "no cookbook is running on this device" });

        if (!runner.TryCancel(id)) return StatusCode(409, new { error = "no cookbook is running on this device" });
        return Ok(new { ok = true, jobId = latest.Id });
    }

    [HttpGet("{id}/cookbooks/run/{jobId:long}")]
    public async Task<IActionResult> Run(string id, long jobId, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        if (Cookbooks is not { } feed) return StatusCode(503, new { error = "no database configured" });

        if (await feed.RunAsync(id, jobId, ct) is not { } status)
            return NotFound(new { error = "unknown cookbook run for this device" });

        return Ok(status);
    }
}

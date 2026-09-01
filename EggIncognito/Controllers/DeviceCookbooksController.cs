using System.Text.Json;
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
        if (Timeline is not { } timeline) return StatusCode(503, new { error = "no database configured" });

        var history = await timeline.HistoryAsync(id, 50, DeviceJobKinds.Cookbook, ct);
        if (history.FirstOrDefault(j => j.Id == jobId) is not { } job)
            return NotFound(new { error = "unknown cookbook run for this device" });

        var lines = await timeline.LinesAsync(id, job.Id, ct);
        return Ok(new DeviceCookbookRunView(
            job.Id, job.DeviceId, job.State, job.Outcome, job.Message, job.StartedAt, job.FinishedAt,
            job.State == DeviceJobStates.Running,
            [.. lines.Select(l => l.Text)],
            ParseSteps(job.Detail)));
    }

    private static List<CookbookStepResult>? ParseSteps(string? detail) {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        try {
            using var doc = JsonDocument.Parse(detail);
            if (!doc.RootElement.TryGetProperty("steps", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<CookbookStepResult>();
            foreach (var el in arr.EnumerateArray()) {
                string stepId = el.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                string title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                string? note = el.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()
                    : null;
                var status = el.TryGetProperty("status", out var s)
                             && Enum.TryParse<CookbookStepStatus>(s.GetString(), out var parsed)
                    ? parsed
                    : CookbookStepStatus.Ok;
                list.Add(new CookbookStepResult(stepId, title, status, note, []));
            }

            return list.Count > 0 ? list : null;
        } catch (JsonException) {
            return null;
        }
    }
}

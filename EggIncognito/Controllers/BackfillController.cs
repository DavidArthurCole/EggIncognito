using EggIncognito.Data.Models;
using EggIncognito.Services;
using EggIncognito.Services.Backfill;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

// Admin-only proto-backfill triggers. Each mirrors AdminController's gating: the role check runs before
// the DB resolve, so a non-admin 403s and a no-DB caller 503s regardless of state. The importers run in
// the background (they open their own DI scope), so the request returns immediately. Not public.
[ApiController]
[Route("api/protos/backfill")]
[EnableRateLimiting("write")]
public sealed class BackfillController(IServiceProvider services, ICurrentUser user) : ControllerBase
{
    private IActionResult? RequireAdmin() =>
        user.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin only" });

    [HttpPost("elgranjero")]
    public IActionResult Elgranjero()
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(ElgranjeroImporter)) is not ElgranjeroImporter importer)
            return StatusCode(503, new { error = "backfill not available (no DB)" });
        _ = Task.Run(() => importer.RunAsync(CancellationToken.None));
        return Accepted(new { status = "elgranjero import started" });
    }

    [HttpPost("playstore")]
    public IActionResult PlayStore()
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(PlayStoreImporter)) is not PlayStoreImporter importer)
            return StatusCode(503, new { error = "backfill not available (no DB)" });
        _ = Task.Run(() => importer.RunAsync(CancellationToken.None));
        return Accepted(new { status = "playstore import started" });
    }

    [HttpPost("appstore")]
    public IActionResult AppStore()
    {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(AppStoreImporter)) is not AppStoreImporter importer)
            return StatusCode(503, new { error = "backfill not available (no DB)" });
        _ = Task.Run(() => importer.RunAsync(CancellationToken.None));
        return Accepted(new { status = "appstore import started" });
    }
}

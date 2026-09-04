using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/apks")]
[ApiAccess(ApiAccessLevel.Admin)]
public sealed class ApksController(IServiceProvider services, ICurrentUser currentUser) : ControllerBase {
    private ApkStore? Store => services.GetService(typeof(ApkStore)) as ApkStore;

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List(CancellationToken ct) {
        if (!currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        var sets = await store.AllVersionsAsync(ct);
        var versions = sets.Select(Shape).ToList();
        return Ok(new StoredApkList(true, versions.Count, versions));
    }

    [HttpDelete]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete([FromQuery] string platform, [FromQuery] string package,
        [FromQuery] string appVersion, [FromQuery] string build, CancellationToken ct) {
        if (!currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (Store is not { } store) return StatusCode(503, new { error = "no database configured" });
        int removed = await store.DeleteVersionAsync(platform, package, appVersion, build, ct);
        return removed > 0
            ? Ok(new { ok = true, platform, package, appVersion, build, removed })
            : NotFound(new { ok = false, error = $"no stored apk {package} {appVersion} ({build})" });
    }

    private static StoredApkVersionRow Shape(ApkVersionSet set) => new(
        set.Platform, set.Package, set.AppVersion, set.Build, set.ByteSize, set.Installable, set.CapturedAt,
        [.. set.Splits.Select(s => new StoredApkSplitRow(s.Split, s.Sha256, s.ByteSize, s.SourceDeviceId,
            s.CapturedAt))]);
}

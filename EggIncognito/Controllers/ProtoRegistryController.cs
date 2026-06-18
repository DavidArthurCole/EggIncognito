using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

// Write surface over the proto registry, split from the public read-only ProtosController. Gating
// mirrors BackfillController: the role check runs before the DB resolve, so insufficient role 403s and a
// no-DB host 503s regardless of state. Save + edit are additive (contributor+); delete + merge are
// destructive (admin). Deletes are soft (DeletedAt) so the auto-importers cannot resurrect them.
[ApiController]
[Route("api/protos/versions")]
[EnableRateLimiting("write")]
public sealed class ProtoRegistryController(IServiceProvider services, ICurrentUser user) : ControllerBase
{
    private const string NoDb = "registry not available (no DB)";

    private ProtoRegistryStore? Store => services.GetService(typeof(ProtoRegistryStore)) as ProtoRegistryStore;

    private IActionResult? Require(UserRole role) =>
        user.IsAtLeast(role) ? null : StatusCode(403, new { error = $"{UserRoles.ToName(role)}+ only" });

    public sealed record SaveRequest(string Platform, string AppVersion, string Build, string? ClientVersion,
        string? Package, string Proto, string? Source);

    // Promote an uploaded/analyzed extraction into the registry. Contributor+. Computes the SHA + index
    // from the proto text so the row matches farm-ingested rows. Source defaults to "upload".
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        if (string.IsNullOrWhiteSpace(req.Build) || string.IsNullOrWhiteSpace(req.AppVersion) || string.IsNullOrWhiteSpace(req.Proto))
            return StatusCode(400, new { error = "platform, appVersion, build, proto required" });

        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(req.Proto))).ToLowerInvariant();
        var (row, created, _) = await store.UpsertAsync(
            req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package ?? "",
            sha, apkRef: "", DateTimeOffset.UtcNow, user.Username, req.Proto, req.Source ?? "upload", ct: ct);
        return Ok(new { ok = true, created, row.Platform, row.Build, protoSha = sha });
    }

    public sealed record EditRequest(string? AppVersion, string? ClientVersion, string? Source);

    // Correct/annotate a stored version's metadata. Contributor+. Null fields are left unchanged.
    [HttpPatch("{platform}/{build}")]
    public async Task<IActionResult> Edit(string platform, string build, [FromBody] EditRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var ok = await store.UpdateMetadataAsync(platform, build, req.AppVersion, req.ClientVersion, req.Source, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }

    // Soft-delete one stored version. Admin. The row is hidden, not removed, so re-ingest cannot revive it.
    [HttpDelete("{platform}/{build}")]
    public async Task<IActionResult> Delete(string platform, string build, CancellationToken ct)
    {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var ok = await store.SoftDeleteAsync(platform, build, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }

    public sealed record VersionKey(string Platform, string Build);
    public sealed record BulkDeleteRequest(IReadOnlyList<VersionKey> Versions);

    // Soft-delete many. Admin. Returns the count deleted.
    [HttpPost("delete")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var deleted = 0;
        foreach (var v in req.Versions ?? [])
            if (await store.SoftDeleteAsync(v.Platform, v.Build, ct)) deleted++;
        return Ok(new { ok = true, deleted });
    }

    public sealed record MergeRequest(VersionKey Canonical, IReadOnlyList<VersionKey> Aliases);

    // Merge aliases into a canonical version (same schema, possibly cross-platform). Admin. Aliases become
    // hidden pointers to the canonical; reversible via restore.
    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] MergeRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        if (req?.Canonical is null || req.Aliases is null || req.Aliases.Count == 0)
            return StatusCode(400, new { error = "canonical + at least one alias required" });
        var aliases = req.Aliases.Select(a => (a.Platform, a.Build)).ToList();
        var linked = await store.MergeAsync((req.Canonical.Platform, req.Canonical.Build), aliases, ct);
        return Ok(new { ok = true, linked });
    }

    // Restore a soft-deleted / merged version. Admin.
    [HttpPost("{platform}/{build}/restore")]
    public async Task<IActionResult> Restore(string platform, string build, CancellationToken ct)
    {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var ok = await store.RestoreAsync(platform, build, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }
}

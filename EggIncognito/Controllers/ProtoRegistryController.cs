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

    public sealed record EditRequest(string? AppVersion, string? ClientVersion, string? Source, string? Build);

    // Correct/annotate a stored version's metadata. Contributor+. Null fields are left unchanged. Build re-keys
    // the row when given + different; a collision (another row already has that build for the platform) -> 409.
    [HttpPatch("{platform}/{build}")]
    public async Task<IActionResult> Edit(string platform, string build, [FromBody] EditRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var result = await store.UpdateMetadataAsync(
            platform, build, req.AppVersion, req.ClientVersion, req.Source, req.Build, ct);
        return result switch
        {
            ProtoRegistryStore.MetadataUpdate.Ok => Ok(new { ok = true }),
            ProtoRegistryStore.MetadataUpdate.BuildCollision =>
                Conflict(new { error = $"build '{req.Build}' already exists for {platform}" }),
            _ => NotFound(),
        };
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

    // Suggested cross-platform merges: groups of active rows sharing app version + proto SHA across >=2
    // platforms (e.g. the iOS + Android build of one release). Read-only; the UI offers a one-click merge.
    // Public read like the version list (the merge action itself is admin-gated).
    [HttpGet("merge-suggestions")]
    public async Task<IActionResult> MergeSuggestions(CancellationToken ct)
    {
        if (Store is not { } store) return Ok(Array.Empty<object>());
        var suggestions = await store.SuggestMergesAsync(ct);
        return Ok(suggestions);
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

    // Staged submissions: review queue between proto sources and the live registry. Routes are absolute
    // (/api/protos/staged/*) so they sit beside the versions surface rather than under it.
    private StagedProtoStore? StagedStore => services.GetService(typeof(StagedProtoStore)) as StagedProtoStore;

    public sealed record OfferRequest(string Platform, string? AppVersion, string? Build,
        string? ClientVersion, string? Package, string ProtoSha, string ProtoText, string? MessageIndex);

    // Public: does this proto already exist (registry or pending)? Drives the analyze-result Offer button.
    [HttpGet("/api/protos/staged/check")]
    public async Task<IActionResult> StagedCheck(
        [FromQuery] string platform, [FromQuery] string? appVersion, [FromQuery] string protoSha, CancellationToken ct)
    {
        if (StagedStore is not { } s) return Ok(new { inRegistry = false, pending = false });
        var (inReg, pending) = await s.CheckAsync(platform, appVersion, protoSha, ct);
        return Ok(new { inRegistry = inReg, pending });
    }

    // Public (rate-limited): submit a proto for review. Dedup-guarded server-side.
    [HttpPost("/api/protos/staged/offer")]
    public async Task<IActionResult> StagedOffer([FromBody] OfferRequest req, CancellationToken ct)
    {
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        if (string.IsNullOrEmpty(req.ProtoSha) || string.IsNullOrEmpty(req.ProtoText))
            return BadRequest(new { error = "protoSha + protoText required" });
        var by = user.DiscordId; // null when anonymous
        var r = await s.OfferAsync(req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package,
            req.ProtoSha, req.ProtoText, req.MessageIndex, by, ct);
        return Ok(new { result = r.ToString().ToLowerInvariant() });
    }

    // Public: pending count for the review badge (0 when no DB).
    [HttpGet("/api/protos/staged/count")]
    public async Task<IActionResult> StagedCount(CancellationToken ct)
    {
        if (StagedStore is not { } s) return Ok(new { count = 0 });
        return Ok(new { count = await s.PendingCountAsync(ct) });
    }

    // Contributor+: the pending review queue.
    [HttpGet("/api/protos/staged")]
    public async Task<IActionResult> StagedList(CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return Ok(Array.Empty<object>());
        var rows = await s.PendingAsync(ct);
        return Ok(rows.Select(r => new
        {
            id = r.Id, source = r.Source, platform = r.Platform, appVersion = r.AppVersion, build = r.Build,
            clientVersion = r.ClientVersion, protoSha = r.ProtoSha, submittedBy = r.SubmittedBy,
            submittedAt = r.SubmittedAt, originRepo = r.OriginRepo, originCommit = r.OriginCommit,
            confidence = r.Confidence,
        }));
    }

    public sealed record ApproveRequest(string? Platform, string? AppVersion, string? Build, string? ClientVersion);

    // Contributor+: approve (with optional metadata edits) -> promotes to the registry.
    [HttpPost("/api/protos/staged/{id:int}/approve")]
    public async Task<IActionResult> StagedApprove(int id, [FromBody] ApproveRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        var who = user.DiscordId ?? "?";
        var r = await s.ApproveAsync(id, req.Platform, req.AppVersion, req.Build, req.ClientVersion, who, ct);
        return r switch
        {
            StagedProtoStore.ApproveResult.Ok => Ok(new { ok = true }),
            StagedProtoStore.ApproveResult.BuildCollision => Conflict(new { error = "build already exists" }),
            StagedProtoStore.ApproveResult.MissingBuild => BadRequest(new { error = "appVersion + build required to approve" }),
            _ => NotFound(),
        };
    }

    public sealed record RejectRequest(string? Note);

    // Contributor+: reject (hidden, blocks re-offer of that sha).
    [HttpPost("/api/protos/staged/{id:int}/reject")]
    public async Task<IActionResult> StagedReject(int id, [FromBody] RejectRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        var who = user.DiscordId ?? "?";
        return await s.RejectAsync(id, req.Note, who, ct) ? Ok(new { ok = true }) : NotFound();
    }

    public sealed record BulkApproveItem(int Id, string? Platform, string? AppVersion, string? Build, string? ClientVersion);
    public sealed record BulkApproveRequest(IReadOnlyList<BulkApproveItem> Items);

    // Contributor+: approve many staged rows with their (edited) metadata. Rows that can't promote (missing
    // build, collision) are skipped, not fatal. Returns per-outcome counts.
    [HttpPost("/api/protos/staged/bulk-approve")]
    public async Task<IActionResult> StagedBulkApprove([FromBody] BulkApproveRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        var who = user.DiscordId ?? "?";
        var items = (req.Items ?? [])
            .Select(i => new StagedProtoStore.ApproveItem(i.Id, i.Platform, i.AppVersion, i.Build, i.ClientVersion))
            .ToList();
        var r = await s.BulkApproveAsync(items, who, ct);
        return Ok(new { ok = true, approved = r.Approved, skipped = r.Skipped, failed = r.Failed });
    }

    public sealed record BulkRejectRequest(IReadOnlyList<int> Ids, string? Note);

    // Contributor+: reject many staged rows at once. Returns the count rejected.
    [HttpPost("/api/protos/staged/bulk-reject")]
    public async Task<IActionResult> StagedBulkReject([FromBody] BulkRejectRequest req, CancellationToken ct)
    {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        var who = user.DiscordId ?? "?";
        var rejected = await s.BulkRejectAsync(req.Ids ?? [], req.Note, who, ct);
        return Ok(new { ok = true, rejected });
    }

    // Admin: bulk-import the GitHub-crawl backfill dataset (zip of manifest.json + snapshots/) into staging.
    [HttpPost("/api/protos/staged/import-crawl")]
    public async Task<IActionResult> ImportCrawl(IFormFile file, CancellationToken ct)
    {
        if (Require(UserRole.Admin) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        if (file is null || file.Length == 0) return BadRequest(new { error = "zip file required" });
        byte[] bytes;
        using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); bytes = ms.ToArray(); }
        IReadOnlyList<EggIncognito.Core.Services.Protos.CrawlManifestReader.CrawlRecord> records;
        try { records = EggIncognito.Core.Services.Protos.CrawlManifestReader.Read(bytes); }
        catch (Exception ex) { return BadRequest(new { error = $"bad dataset zip: {ex.Message}" }); }
        var (staged, skipped) = await s.ImportCrawlAsync(records, ct);
        return Ok(new { staged, skipped });
    }
}

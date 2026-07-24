using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyncKit.Contract;

namespace EggIncognito.Controllers;


[ApiController]
[Route("api/protos/versions")]
[EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Public)]
[EnableRateLimiting("write")]
public sealed class ProtoRegistryController(IServiceProvider services, ICurrentUser user) : ControllerBase {
    private const string NoDb = "registry not available (no DB)";

    private ProtoRegistryStore? Store => services.GetService(typeof(ProtoRegistryStore)) as ProtoRegistryStore;

    private ObjectResult? Require(UserRole role) =>
        user.IsAtLeast(role) ? null : StatusCode(403, new { error = $"{UserRoles.ToName(role)}+ only" });

    public sealed record SaveRequest(string Platform, string AppVersion, string Build, string? ClientVersion,
        string? Package, string Proto, string? Source);



    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        if (string.IsNullOrWhiteSpace(req.Build) || string.IsNullOrWhiteSpace(req.AppVersion) || string.IsNullOrWhiteSpace(req.Proto))
            return StatusCode(400, new { error = "platform, appVersion, build, proto required" });

        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(req.Proto)));
        var (row, created, _) = await store.UpsertAsync(
            req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package ?? "",
            sha, apkRef: "", DateTimeOffset.UtcNow, user.Username, req.Proto, req.Source ?? "upload", ct: ct);
        return Ok(new { ok = true, created, row.Platform, row.Build, protoSha = sha });
    }

    public sealed record EditRequest(string? AppVersion, string? ClientVersion, string? Source, string? Build);



    [HttpPatch("{platform}/{build}")]
    public async Task<IActionResult> Edit(string platform, string build, [FromBody] EditRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var result = await store.UpdateMetadataAsync(
            platform, build, req.AppVersion, req.ClientVersion, req.Source, req.Build, ct);
        return result switch {
            ProtoRegistryStore.MetadataUpdate.Ok => Ok(new { ok = true }),
            ProtoRegistryStore.MetadataUpdate.BuildCollision =>
                Conflict(new { error = $"build '{req.Build}' already exists for {platform}" }),
            _ => NotFound(),
        };
    }


    [HttpDelete("{platform}/{build}")]
    public async Task<IActionResult> Delete(string platform, string build, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var ok = await store.SoftDeleteAsync(platform, build, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }

    public sealed record VersionKey(string Platform, string Build);
    public sealed record BulkDeleteRequest(IReadOnlyList<VersionKey> Versions);


    [HttpPost("delete")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest req, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var deleted = 0;
        foreach (var v in req.Versions ?? [])
            if (await store.SoftDeleteAsync(v.Platform, v.Build, ct)) deleted++;
        return Ok(new { ok = true, deleted });
    }




    [HttpGet("merge-suggestions")]
    [EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> MergeSuggestions(CancellationToken ct) {
        if (Store is not { } store) return Ok(Array.Empty<object>());
        var suggestions = await store.SuggestMergesAsync(ct);
        return Ok(suggestions);
    }

    public sealed record MergeRequest(VersionKey Canonical, IReadOnlyList<VersionKey> Aliases);



    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] MergeRequest req, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        if (req?.Canonical is null || req.Aliases is null || req.Aliases.Count == 0)
            return StatusCode(400, new { error = "canonical + at least one alias required" });
        var aliases = req.Aliases.Select(a => (a.Platform, a.Build)).ToList();
        var linked = await store.MergeAsync((req.Canonical.Platform, req.Canonical.Build), aliases, ct);
        return Ok(new { ok = true, linked });
    }


    [HttpPost("{platform}/{build}/restore")]
    public async Task<IActionResult> Restore(string platform, string build, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var ok = await store.RestoreAsync(platform, build, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }



    private StagedProtoStore? StagedStore => services.GetService(typeof(StagedProtoStore)) as StagedProtoStore;

    public sealed record OfferRequest(string Platform, string? AppVersion, string? Build,
        string? ClientVersion, string? Package, string ProtoSha, string ProtoText, string? MessageIndex);


    [HttpGet("/api/protos/staged/check")]
    [EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> StagedCheck([FromQuery] string protoSha, CancellationToken ct) {
        if (StagedStore is not { } s) return Ok(new { inRegistry = false, pending = false });
        var (inReg, pending) = await s.CheckAsync(protoSha, ct);
        return Ok(new { inRegistry = inReg, pending });
    }


    [HttpPost("/api/protos/staged/offer")]
    public async Task<IActionResult> StagedOffer([FromBody] OfferRequest req, CancellationToken ct) {
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        if (string.IsNullOrEmpty(req.ProtoSha) || string.IsNullOrEmpty(req.ProtoText))
            return BadRequest(new { error = "protoSha + protoText required" });
        var by = user.DiscordId;
        var r = await s.OfferAsync(req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package,
            req.ProtoSha, req.ProtoText, req.MessageIndex, by, ct);
        return Ok(new { result = r.ToString().ToLowerInvariant() });
    }


    [HttpGet("/api/protos/staged/count")]
    [EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> StagedCount(CancellationToken ct) {
        if (StagedStore is not { } s) return Ok(new { count = 0 });
        return Ok(new { count = await s.PendingCountAsync(ct) });
    }


    [HttpGet("/api/protos/staged")]
    public async Task<IActionResult> StagedList(CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return Ok(Array.Empty<object>());
        var rows = await s.PendingAsync(ct);
        return Ok(rows.Select(r => new {
            id = r.Id,
            source = r.Source,
            platform = r.Platform,
            appVersion = r.AppVersion,
            build = r.Build,
            clientVersion = r.ClientVersion,
            protoSha = r.ProtoSha,
            submittedBy = r.SubmittedBy,
            submittedAt = r.SubmittedAt,
            originRepo = r.OriginRepo,
            originCommit = r.OriginCommit,
            originDate = r.OriginDate,
            confidence = r.Confidence,
        }));
    }

    public sealed record ApproveRequest(string? Platform, string? AppVersion, string? Build, string? ClientVersion);


    [HttpPost("/api/protos/staged/{id:int}/approve")]
    public async Task<IActionResult> StagedApprove(int id, [FromBody] ApproveRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        var who = user.DiscordId ?? "?";
        var r = await s.ApproveAsync(id, req.Platform, req.AppVersion, req.Build, req.ClientVersion, who, ct);
        return r switch {
            StagedProtoStore.ApproveResult.Ok => Ok(new { ok = true, merged = false }),
            StagedProtoStore.ApproveResult.Merged => Ok(new { ok = true, merged = true }),
            StagedProtoStore.ApproveResult.MissingBuild => BadRequest(new { error = "appVersion + build required to approve" }),
            _ => NotFound(),
        };
    }

    public sealed record RejectRequest(string? Note);


    [HttpPost("/api/protos/staged/{id:int}/reject")]
    public async Task<IActionResult> StagedReject(int id, [FromBody] RejectRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        var who = user.DiscordId ?? "?";
        return await s.RejectAsync(id, req.Note, who, ct) ? Ok(new { ok = true }) : NotFound();
    }

    public sealed record BulkApproveItem(int Id, string? Platform, string? AppVersion, string? Build, string? ClientVersion);
    public sealed record BulkApproveRequest(IReadOnlyList<BulkApproveItem> Items);



    [HttpPost("/api/protos/staged/bulk-approve")]
    public async Task<IActionResult> StagedBulkApprove([FromBody] BulkApproveRequest req, CancellationToken ct) {
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


    [HttpPost("/api/protos/staged/bulk-reject")]
    public async Task<IActionResult> StagedBulkReject([FromBody] BulkRejectRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        var who = user.DiscordId ?? "?";
        var rejected = await s.BulkRejectAsync(req.Ids ?? [], req.Note, who, ct);
        return Ok(new { ok = true, rejected });
    }


    [HttpPost("/api/protos/staged/import-crawl")]
    public async Task<IActionResult> ImportCrawl(IFormFile file, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (StagedStore is not { } s) return StatusCode(503, new { error = NoDb });
        if (file is null || file.Length == 0) return BadRequest(new { error = "zip file required" });
        byte[] bytes;
        using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); bytes = ms.ToArray(); }
        IReadOnlyList<EggIncognito.Core.Services.Protos.CrawlManifestReader.CrawlRecord> records;
        try { records = EggIncognito.Core.Services.Protos.CrawlManifestReader.Read(bytes); } catch (Exception ex) { return BadRequest(new { error = $"bad dataset zip: {ex.Message}" }); }
        var (staged, skipped) = await s.ImportCrawlAsync(records, ct);
        return Ok(new { staged, skipped });
    }
}

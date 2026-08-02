using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos/batch")]
[ApiAccess(ApiAccessLevel.Authenticated)]
[EnableRateLimiting("write")]
public sealed class ProtoBatchController(IServiceProvider services, ICurrentUser user) : ControllerBase {
    private const int MaxFiles = 50;
    private const long MaxPerFileBytes = 200_000_000;
    private const long MaxTotalBytes = 1_000_000_000;
    private const string NoDb = "batch upload not available (no DB)";

    private UploadBatchStore? Store => services.GetService(typeof(UploadBatchStore)) as UploadBatchStore;

    private bool CanUpload => user.IsAtLeast(UserRole.Contributor) || user.IsSupporter;

    [HttpPost]
    [RequestSizeLimit(MaxTotalBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxTotalBytes)]
    public async Task<IActionResult> Upload([FromForm] IFormFileCollection files, CancellationToken ct) {
        if (!CanUpload) return StatusCode(403, new { error = "contributor or supporter only" });
        if (files is null || files.Count == 0) return BadRequest(new { error = "no files uploaded" });
        if (files.Count > MaxFiles) return BadRequest(new { error = $"max {MaxFiles} files per batch" });

        long total = 0;
        foreach (var f in files) {
            if (f.Length > MaxPerFileBytes) return BadRequest(new { error = $"{f.FileName} exceeds per-file limit" });
            total += f.Length;
        }
        if (total > MaxTotalBytes) return BadRequest(new { error = "batch exceeds total size limit" });

        if (Store is not { } store) return StatusCode(503, new { error = NoDb });

        var items = new List<UploadBatchStore.NewBatchFile>(files.Count);
        foreach (var f in files) {
            byte[] bytes = new byte[f.Length];
            using (var dest = new MemoryStream(bytes)) await f.CopyToAsync(dest, ct);
            items.Add(new UploadBatchStore.NewBatchFile(f.FileName, f.Length, bytes));
        }

        int batchId = await store.CreateAsync(user.DiscordId, items, ct);
        return Ok(new { batchId, itemCount = items.Count });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) {
        if (Store is not { } store) return StatusCode(503, new { error = NoDb });
        var view = await store.GetAsync(id, ct);
        if (view is null) return NotFound();
        bool owner = view.SubmittedBy is { } sb && sb == user.DiscordId;
        if (!owner && !user.IsAtLeast(UserRole.Contributor))
            return StatusCode(403, new { error = "not your batch" });
        return Ok(new {
            id = view.Id,
            status = view.Status,
            total = view.TotalItems,
            processed = view.ProcessedItems,
            items = view.Items.Select(i => new {
                i.Id,
                i.FileName,
                i.Status,
                i.Platform,
                i.ProtoSha,
                i.AppVersion,
                i.Build,
                i.ClientVersion,
                i.Diagnostics
            })
        });
    }
}

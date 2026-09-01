using System.Text.Json;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record ImageBuildHead(
    long Id, string Tag, string State, string? Note, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);

public sealed record ImageBuildDetail(
    long Id, string Spec, string Tag, string State, string? Note, string Log,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);

public sealed class ImageBuildStore(EggIncognitoDbContext db, TimeProvider time) {
    public async Task<long> CreateAsync(ImageBuildSpec spec, CancellationToken ct) {
        var row = new ImageBuild {
            Spec = JsonSerializer.Serialize(spec),
            Tag = spec.ResolvedTag,
            State = ImageBuildStates.Queued,
            Log = "",
            StartedAt = time.GetUtcNow()
        };
        db.ImageBuilds.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    public Task AppendAsync(long id, string line, CancellationToken ct) =>
        db.ImageBuilds.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Log, x => x.Log + line + "\n"), ct);

    public Task SetStateAsync(long id, string state, string? note, CancellationToken ct) =>
        db.ImageBuilds.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, state)
                .SetProperty(x => x.Note, note), ct);

    public Task FinishAsync(long id, string state, string? tag, string? note, CancellationToken ct) =>
        db.ImageBuilds.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, state)
                .SetProperty(x => x.Tag, x => tag ?? x.Tag)
                .SetProperty(x => x.Note, note)
                .SetProperty(x => x.FinishedAt, time.GetUtcNow()), ct);

    public Task<ImageBuildDetail?> GetAsync(long id, CancellationToken ct) =>
        db.ImageBuilds.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ImageBuildDetail(
                x.Id, x.Spec, x.Tag, x.State, x.Note, x.Log, x.StartedAt, x.FinishedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ImageBuildHead>> RecentAsync(int n, CancellationToken ct) {
        var rows = await db.ImageBuilds.AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(Math.Clamp(n, 1, 100))
            .Select(x => new ImageBuildHead(x.Id, x.Tag, x.State, x.Note, x.StartedAt, x.FinishedAt))
            .ToListAsync(ct);
        return rows;
    }
}

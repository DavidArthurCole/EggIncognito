using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Persistence the backfill importers depend on: a job-progress row per run plus the known-versions
// discovery list. Extracted as an interface so importers can be unit-tested against a fake, DB-free.
public interface IBackfillJobStore
{
    Task<BackfillJob> StartAsync(string source, string? startedBy, CancellationToken ct = default);
    Task BumpAsync(int jobId, int imported, string? note = null, CancellationToken ct = default);
    Task FinishAsync(int jobId, string status, string? note = null, CancellationToken ct = default);
    Task<List<BackfillJob>> LatestPerSourceAsync(CancellationToken ct = default);

    Task UpsertKnownAsync(
        string platform, string appVersion, DateTimeOffset? releaseDate, string? changelog, string source,
        CancellationToken ct = default);
    Task<List<KnownVersion>> KnownAsync(CancellationToken ct = default);

    Task StartExtractAsync(string platform, string appVersion, CancellationToken ct = default);
    Task FinishExtractAsync(string platform, string appVersion, string status, string? note,
        CancellationToken ct = default);
    Task<List<ExtractJob>> ListExtractJobsAsync(CancellationToken ct = default);
}

public sealed class BackfillJobStore(EggIncognitoDbContext db) : IBackfillJobStore
{
    public async Task<BackfillJob> StartAsync(string source, string? startedBy, CancellationToken ct = default)
    {
        var job = new BackfillJob
        {
            Source = source,
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            Imported = 0,
            StartedBy = startedBy,
        };
        db.BackfillJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public async Task BumpAsync(int jobId, int imported, string? note = null, CancellationToken ct = default)
    {
        var job = await db.BackfillJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return;
        job.Imported = imported;
        if (note is not null) job.Note = note;
        await db.SaveChangesAsync(ct);
    }

    public async Task FinishAsync(int jobId, string status, string? note = null, CancellationToken ct = default)
    {
        var job = await db.BackfillJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return;
        job.Status = status;
        job.FinishedAt = DateTimeOffset.UtcNow;
        if (note is not null) job.Note = note;
        await db.SaveChangesAsync(ct);
    }

    // The most recent run per source, newest start first. The UI polls this for live status.
    public async Task<List<BackfillJob>> LatestPerSourceAsync(CancellationToken ct = default)
    {
        var rows = await db.BackfillJobs.AsNoTracking()
            .GroupBy(j => j.Source)
            .Select(g => g.OrderByDescending(j => j.StartedAt).First())
            .ToListAsync(ct);
        return rows.OrderByDescending(j => j.StartedAt).ToList();
    }

    public async Task UpsertKnownAsync(
        string platform, string appVersion, DateTimeOffset? releaseDate, string? changelog, string source,
        CancellationToken ct = default)
    {
        var row = await db.KnownVersions.FirstOrDefaultAsync(
            k => k.Platform == platform && k.AppVersion == appVersion && k.Source == source, ct);
        if (row is null)
        {
            row = new KnownVersion
            {
                Platform = platform,
                AppVersion = appVersion,
                Source = source,
                FirstSeen = DateTimeOffset.UtcNow,
            };
            db.KnownVersions.Add(row);
        }
        // Refresh metadata when the source re-reports it; first_seen stays at the original sighting.
        if (releaseDate is not null) row.ReleaseDate = releaseDate;
        if (!string.IsNullOrEmpty(changelog)) row.Changelog = changelog;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<KnownVersion>> KnownAsync(CancellationToken ct = default) =>
        db.KnownVersions.AsNoTracking()
            .OrderByDescending(k => k.FirstSeen)
            .ToListAsync(ct);

    // One job per (platform, appVersion); a re-extract resets the existing row rather than stacking.
    public async Task StartExtractAsync(string platform, string appVersion, CancellationToken ct = default)
    {
        var row = await db.ExtractJobs.FirstOrDefaultAsync(
            e => e.Platform == platform && e.AppVersion == appVersion, ct);
        if (row is null)
        {
            row = new ExtractJob { Platform = platform, AppVersion = appVersion };
            db.ExtractJobs.Add(row);
        }
        row.Status = "running";
        row.StartedAt = DateTimeOffset.UtcNow;
        row.FinishedAt = null;
        row.Note = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task FinishExtractAsync(
        string platform, string appVersion, string status, string? note, CancellationToken ct = default)
    {
        var row = await db.ExtractJobs.FirstOrDefaultAsync(
            e => e.Platform == platform && e.AppVersion == appVersion, ct);
        if (row is null) return;
        row.Status = status;
        row.FinishedAt = DateTimeOffset.UtcNow;
        row.Note = note;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<ExtractJob>> ListExtractJobsAsync(CancellationToken ct = default) =>
        db.ExtractJobs.AsNoTracking()
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync(ct);
}

using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

// Shared "is the store ahead of what is installed?" computation, so the auto-path and the manual admin
// Update endpoint agree on when an update is warranted.
public static class StoreAheadCheck
{
    // Picks the true semver max, not the most-recently-seen row.
    public static async Task<string?> StoreLatestAsync(EggIncognitoDbContext db, string platform, CancellationToken ct)
    {
        var versions = await db.KnownVersions.AsNoTracking()
            .Where(k => k.Platform == platform)
            .Select(k => k.AppVersion)
            .ToListAsync(ct);
        return versions
            .OrderByDescending(v => v, Comparer<string>.Create(DeviceProbeRunner.SemverCompare))
            .FirstOrDefault();
    }

    public static bool IsAhead(string? storeLatest, string? installed) =>
        !string.IsNullOrEmpty(storeLatest) && !string.IsNullOrEmpty(installed)
        && DeviceProbeRunner.SemverCompare(storeLatest!, installed!) > 0;
}

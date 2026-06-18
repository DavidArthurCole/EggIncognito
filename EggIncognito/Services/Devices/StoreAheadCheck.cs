using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

// Shared "is the store ahead of what is installed?" computation. store-latest = the newest app version
// VersionPollerService has discovered (known_versions). Used by both the auto-path (RealDeviceUpgrader)
// and the manual admin Update endpoint, so the two agree on when an update is warranted. DB-gated.
public static class StoreAheadCheck
{
    // Newest store-known app version for a platform, or null if none. Picks the true semver max, not the
    // most-recently-seen row.
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

    // True when the store's newest version is strictly ahead of installed. Both args non-empty required.
    public static bool IsAhead(string? storeLatest, string? installed) =>
        !string.IsNullOrEmpty(storeLatest) && !string.IsNullOrEmpty(installed)
        && DeviceProbeRunner.SemverCompare(storeLatest!, installed!) > 0;
}

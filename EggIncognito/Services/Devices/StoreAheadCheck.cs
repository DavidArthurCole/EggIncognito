using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public static class StoreAheadCheck {

    public static async Task<string?> StoreLatestAsync(EggIncognitoDbContext db, string platform, CancellationToken ct) {
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

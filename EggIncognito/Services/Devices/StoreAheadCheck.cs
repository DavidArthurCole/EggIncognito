using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public static class StoreAheadCheck {
    public static async Task<string?> StoreLatestAsync(
        EggIncognitoDbContext db, string platform, CancellationToken ct, bool crossPlatformHint = false) {
        var query = db.KnownVersions.AsNoTracking();
        if (!crossPlatformHint) query = query.Where(k => k.Platform == platform);
        var versions = await query
            .Select(k => k.AppVersion)
            .ToListAsync(ct);
        return versions
            .OrderByDescending(v => v, Comparer<string>.Create((x, y) => DeviceParsing.CompareVersions(x, y)))
            .FirstOrDefault();
    }

    public static bool IsAhead(string? storeLatest, string? installed) =>
        !string.IsNullOrEmpty(storeLatest) && !string.IsNullOrEmpty(installed)
                                           && DeviceParsing.CompareVersions(storeLatest, installed) > 0;
}

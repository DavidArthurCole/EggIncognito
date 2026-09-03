using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public sealed class DeviceVersionIndex {
    private static readonly Comparer<string?> VersionOrder =
        Comparer<string?>.Create((x, y) => DeviceParsing.CompareVersions(x, y));

#pragma warning disable IDE0028
    private readonly Dictionary<string, string?> _latestApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _latestBuild = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028
    private readonly Dictionary<(string Platform, string AppVersion), int> _byAppVersion = [];
    private readonly Dictionary<(string Platform, string Build), int> _byBuild = [];

    public static DeviceVersionIndex Empty { get; } = new();

    public static async Task<DeviceVersionIndex> BuildAsync(EggIncognitoDbContext db,
        IEnumerable<string> platforms, CancellationToken ct) {
        var index = new DeviceVersionIndex();
        foreach (string platform in platforms) await index.AddPlatformAsync(db, platform, ct);
        return index;
    }

    public string? LatestAppVersion(string platform) => _latestApp.GetValueOrDefault(platform);

    public string? LatestBuild(string platform) => _latestBuild.GetValueOrDefault(platform);

    public int? ClientVersion(string platform, string? appVersion, string? build) {
        if (build is not null && _byBuild.TryGetValue((platform, build), out int byBuild)) return byBuild;
        if (appVersion is not null && _byAppVersion.TryGetValue((platform, appVersion), out int byApp)) return byApp;
        return null;
    }

    private async Task AddPlatformAsync(EggIncognitoDbContext db, string platform, CancellationToken ct) {
        var extracted = await db.ProtoVersions.AsNoTracking()
            .Where(v => v.Platform == platform && (v.DeletedAt == null || v.CanonicalId != null))
            .Select(v => new { v.Build, v.AppVersion, v.ClientVersion })
            .ToListAsync(ct);

        foreach (var e in extracted.OrderBy(v => v.Build, VersionOrder)) {
            if (!int.TryParse(e.ClientVersion, out int clientVersion)) continue;
            if (!string.IsNullOrEmpty(e.AppVersion)) _byAppVersion[(platform, e.AppVersion)] = clientVersion;
            if (!string.IsNullOrEmpty(e.Build)) _byBuild[(platform, e.Build)] = clientVersion;
        }

        _latestApp[platform] = extracted.Select(e => e.AppVersion).OrderByDescending(v => v, VersionOrder)
            .FirstOrDefault();
        _latestBuild[platform] = Platforms.Matches(platform, Platforms.Android)
            ? extracted.Select(e => e.Build).Where(b => long.TryParse(b, out _)).OrderByDescending(long.Parse)
                .FirstOrDefault()
            : null;
    }
}

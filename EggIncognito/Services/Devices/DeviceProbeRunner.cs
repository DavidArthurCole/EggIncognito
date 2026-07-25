using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public static class DeviceProbeRunner {
    public static IDeviceProbe ProbeFor(Device d, IProcessRunner runner) => d.Platform switch {
        "ios" => new IosDeviceProbe(runner, d.Target, d.Package),
        _ => new AdbDeviceProbe(runner, d.Target, d.Package)
    };

    public static string Classify(Device d, DeviceProbeResult r, string? extractedLatestBuild,
        string? extractedLatestAppVersion)
        => Classify(r, d.Platform, extractedLatestBuild, extractedLatestAppVersion);


    public static string Classify(DeviceProbeResult r, string platform, string? extractedLatestBuild,
        string? extractedLatestAppVersion) {
        if (!r.Reachable) return "unreachable";
        if (string.IsNullOrEmpty(r.InstalledAppVersion)) return "error";

        if (platform == "ios") {
            return extractedLatestAppVersion is null
                ? "new_version"
                : SemverCompare(r.InstalledAppVersion!, extractedLatestAppVersion) > 0
                    ? "new_version"
                    : "no_change";
        }

        return extractedLatestBuild is null
            ? "new_version"
            : long.TryParse(r.InstalledBuild, out long inst) && long.TryParse(extractedLatestBuild, out long ext)
                ? inst > ext ? "new_version" : "no_change"
                : SemverCompare(r.InstalledAppVersion!, extractedLatestAppVersion ?? "") > 0
                    ? "new_version"
                    : "no_change";
    }


    public static int SemverCompare(string a, string b) {
        string[] pa = a.Split('.');
        string[] pb = b.Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++) {
            int x = i < pa.Length && int.TryParse(pa[i], out int xi) ? xi : 0;
            int y = i < pb.Length && int.TryParse(pb[i], out int yi) ? yi : 0;
            if (x != y) return x.CompareTo(y);
        }

        return 0;
    }


    public static async Task<DeviceProbe> ProbeOneAsync(
        Device d, string triggeredBy, IProcessRunner runner, IDeviceStatusStore store,
        EggIncognitoDbContext db, ILogger logger, TimeProvider time, CancellationToken ct) {
        var result = await ProbeFor(d, runner).ProbeAsync(ct);


        var extracted = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.Platform == d.Platform && p.DeletedAt == null)
            .Select(p => new { p.Build, p.AppVersion })
            .ToListAsync(ct);
        string? latestBuild = d.Platform == "android"
            ? extracted.Select(e => e.Build).Where(b => long.TryParse(b, out _)).OrderByDescending(long.Parse)
                .FirstOrDefault()
            : null;
        string? latestAppVersion = extracted.Select(e => e.AppVersion)
            .OrderByDescending(v => v, Comparer<string>.Create(SemverCompare)).FirstOrDefault();
        string? latestAvailable = await db.KnownVersions.AsNoTracking()
            .Where(k => k.Platform == d.Platform)
            .OrderByDescending(k => k.FirstSeen).Select(k => k.AppVersion).FirstOrDefaultAsync(ct);

        string resultCode = Classify(d, result, latestBuild, latestAppVersion);

        var row = new DeviceProbe {
            DeviceId = d.Id,
            ProbedAt = time.GetUtcNow(),
            Reachable = result.Reachable,
            InstalledAppVersion = result.InstalledAppVersion,
            InstalledBuild = result.InstalledBuild,
            LatestAvailable = latestAvailable,
            Result = resultCode,
            TriggeredBy = triggeredBy,
            Note = result.Note
        };
        await store.RecordProbeAsync(row, ct);

        if (result.Reachable) {
            logger.LogInformation("device probe: {Id} reachable installed={App} build={Build} -> {Result}",
                d.Id, result.InstalledAppVersion ?? "?", result.InstalledBuild ?? "-", resultCode);
        } else {
            logger.LogInformation("device probe: {Id} unreachable ({Note})", d.Id, result.Note);
        }

        return row;
    }
}

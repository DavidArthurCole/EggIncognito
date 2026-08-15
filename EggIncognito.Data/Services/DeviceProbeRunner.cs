using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

public static class DeviceProbeRunner {
    public static IDeviceProbe ProbeFor(Device d, IProcessRunner runner) =>
        Platforms.Matches(d.Platform, Platforms.Ios)
            ? new IosDeviceProbe(runner, d.Target, d.Package)
            : new AdbDeviceProbe(runner, d.Target, d.Package);

    public static string Classify(Device d, DeviceProbeResult r, string? extractedLatestBuild,
        string? extractedLatestAppVersion)
        => Classify(r, d.Platform, extractedLatestBuild, extractedLatestAppVersion);


    public static string Classify(DeviceProbeResult r, string platform, string? extractedLatestBuild,
        string? extractedLatestAppVersion) {
        if (!r.Reachable) return "unreachable";
        if (string.IsNullOrEmpty(r.InstalledAppVersion)) return "error";

        if (Platforms.Matches(platform, Platforms.Ios)) {
            return extractedLatestAppVersion is null
                ? "new_version"
                : DeviceParsing.CompareVersions(r.InstalledAppVersion, extractedLatestAppVersion) > 0
                    ? "new_version"
                    : "no_change";
        }

        return extractedLatestBuild is null
            ? "new_version"
            : long.TryParse(r.InstalledBuild, out long inst) && long.TryParse(extractedLatestBuild, out long ext)
                ? inst > ext ? "new_version" : "no_change"
                : DeviceParsing.CompareVersions(r.InstalledAppVersion, extractedLatestAppVersion ?? "") > 0
                    ? "new_version"
                    : "no_change";
    }


    public static async Task<DeviceJobRow> ProbeOneAsync(
        Device d, string triggeredBy, IProcessRunner runner, DeviceJobStore jobs,
        EggIncognitoDbContext db, ILogger logger, TimeProvider time, CancellationToken ct) {
        var result = await ProbeFor(d, runner).ProbeAsync(ct);


        var extracted = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.Platform == d.Platform && p.DeletedAt == null)
            .Select(p => new { p.Build, p.AppVersion })
            .ToListAsync(ct);
        string? latestBuild = Platforms.Matches(d.Platform, Platforms.Android)
            ? extracted.Select(e => e.Build).Where(b => long.TryParse(b, out _)).OrderByDescending(long.Parse)
                .FirstOrDefault()
            : null;
        string? latestAppVersion = extracted.Select(e => e.AppVersion)
            .OrderByDescending(v => v, Comparer<string>.Create((x, y) => DeviceParsing.CompareVersions(x, y)))
            .FirstOrDefault();
        string? latestAvailable = await db.KnownVersions.AsNoTracking()
            .Where(k => k.Platform == d.Platform)
            .OrderByDescending(k => k.FirstSeen).Select(k => k.AppVersion).FirstOrDefaultAsync(ct);

        string resultCode = Classify(d, result, latestBuild, latestAppVersion);

        long id = await jobs.RecordAsync(
            d.Id, DeviceJobKinds.Probe, triggeredBy, resultCode, result.Note,
            new DeviceJobFacts(
                Reachable: result.Reachable,
                AppVersion: result.InstalledAppVersion,
                Build: result.InstalledBuild,
                Detail: latestAvailable is null ? null : new { latestAvailable }),
            ct);

        var now = time.GetUtcNow();
        var row = new DeviceJobRow(
            id, d.Id, DeviceJobKinds.Probe, DeviceJobStates.Succeeded, triggeredBy,
            now, now, resultCode, result.Note,
            result.Reachable, result.InstalledAppVersion, result.InstalledBuild, null, null, null);

        if (result.Reachable) {
            logger.LogInformation("device probe: {Id} reachable installed={App} build={Build} -> {Result}",
                d.Id, result.InstalledAppVersion ?? "?", result.InstalledBuild ?? "-", resultCode);
        } else {
            logger.LogInformation("device probe: {Id} unreachable ({Note})", d.Id, result.Note);
        }

        return row;
    }
}

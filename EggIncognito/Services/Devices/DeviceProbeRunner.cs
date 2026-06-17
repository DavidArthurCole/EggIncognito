using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

// Shared probe path used by both the background service and the admin refresh endpoint, so scheduled and
// manual probes are identical except for provenance (triggeredBy). Classification is per-platform because
// iOS has no build number: android compares numeric build, ios compares semver appVersion.
public static class DeviceProbeRunner
{
    public static IDeviceProbe ProbeFor(Device d, IProcessRunner runner) => d.Platform switch
    {
        "ios" => new IosDeviceProbe(runner, d.Target, d.Package),
        _ => new AdbDeviceProbe(runner, d.Target, d.Package),
    };

    public static string Classify(Device d, DeviceProbeResult r, string? extractedLatestBuild, string? extractedLatestAppVersion)
    {
        if (!r.Reachable) return "unreachable";
        if (string.IsNullOrEmpty(r.InstalledAppVersion)) return "error"; // answered but no version read

        if (d.Platform == "ios")
        {
            if (extractedLatestAppVersion is null) return "new_version";
            return SemverCompare(r.InstalledAppVersion!, extractedLatestAppVersion) > 0 ? "new_version" : "no_change";
        }
        // android: numeric build is authoritative
        if (extractedLatestBuild is null) return "new_version";
        if (long.TryParse(r.InstalledBuild, out var inst) && long.TryParse(extractedLatestBuild, out var ext))
            return inst > ext ? "new_version" : "no_change";
        // build unparseable -> fall back to semver appVersion
        return SemverCompare(r.InstalledAppVersion!, extractedLatestAppVersion ?? "") > 0 ? "new_version" : "no_change";
    }

    // Dotted-numeric compare (1.35.10 > 1.35.9). Mirrors the /protos table's CompareNumeric intent.
    public static int SemverCompare(string a, string b)
    {
        var pa = a.Split('.'); var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length && int.TryParse(pa[i], out var xi) ? xi : 0;
            var y = i < pb.Length && int.TryParse(pb[i], out var yi) ? yi : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    // One full probe: run it, look up extracted + available latest, classify, record, fire upgrader on new.
    public static async Task<DeviceProbe> ProbeOneAsync(
        Device d, string triggeredBy, IProcessRunner runner, IDeviceStatusStore store,
        EggIncognitoDbContext db, IDeviceUpgrader upgrader, ILogger logger, TimeProvider time, CancellationToken ct)
    {
        var result = await ProbeFor(d, runner).ProbeAsync(ct);

        // newest extracted build/appVersion for this platform (drives classification)
        var latestExtracted = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.Platform == d.Platform && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        // newest store-known appVersion for this platform (display only)
        var latestAvailable = await db.KnownVersions.AsNoTracking()
            .Where(k => k.Platform == d.Platform)
            .OrderByDescending(k => k.FirstSeen).Select(k => k.AppVersion).FirstOrDefaultAsync(ct);

        var resultCode = Classify(d, result, latestExtracted?.Build, latestExtracted?.AppVersion);

        var row = new DeviceProbe
        {
            DeviceId = d.Id,
            ProbedAt = time.GetUtcNow(),
            Reachable = result.Reachable,
            InstalledAppVersion = result.InstalledAppVersion,
            InstalledBuild = result.InstalledBuild,
            LatestAvailable = latestAvailable,
            Result = resultCode,
            TriggeredBy = triggeredBy,
            Note = result.Note,
        };
        await store.RecordProbeAsync(row, ct);

        if (result.Reachable)
            logger.LogInformation("device probe: {Id} reachable installed={App} build={Build} -> {Result}",
                d.Id, result.InstalledAppVersion ?? "?", result.InstalledBuild ?? "-", resultCode);
        else
            logger.LogInformation("device probe: {Id} unreachable ({Note})", d.Id, result.Note);

        if (resultCode == "new_version")
            await upgrader.MaybeUpgradeAsync(d, result, ct);

        return row;
    }
}

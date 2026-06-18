using System.IO.Compression;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Services.Backfill.Sources;

namespace EggIncognito.Services.Devices;

// Android zero-touch update: download the target version's XAPK (all split APKs) from APKPure, unzip the
// splits, and `adb install-multiple -r` them onto the rooted device (silent, no on-device tap). Then
// re-probe to confirm the installed version climbed. All via the IProcessRunner seam (no Runner dep).
// Never throws; every failure mode returns a noted outcome so the poll loop keeps going.
public sealed class AndroidDeviceUpdater(
    IProcessRunner runner, IApkDownloader apkPure, ILogger<AndroidDeviceUpdater> logger) : IDeviceUpdater
{
    public async Task<DeviceUpdateOutcome> UpdateAsync(Device device, string targetAppVersion, CancellationToken ct)
    {
        var from = await ReadInstalledAsync(device, ct);
        if (from == targetAppVersion)
            return new DeviceUpdateOutcome(false, true, from, targetAppVersion, "already current");

        logger.LogInformation("device update: {Id} android {From} -> {To}: downloading xapk", device.Id, from, targetAppVersion);
        var xapk = await apkPure.DownloadApkAsync(targetAppVersion, ct);
        if (xapk is null || xapk.Length == 0)
            return new DeviceUpdateOutcome(false, false, from, targetAppVersion, "xapk download failed");

        var dir = Path.Combine(Path.GetTempPath(), $"egi-xapk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var splits = UnzipSplits(xapk, dir);
            if (splits.Count == 0)
                return new DeviceUpdateOutcome(false, false, from, targetAppVersion, "xapk had no apk splits");

            logger.LogInformation("device update: {Id} installing {Count} splits", device.Id, splits.Count);
            // install-multiple -r: reinstall keeping data, all splits in one session (a split app rejects a
            // partial install). Root makes it silent. -d allows a same/lower versionCode if APKPure lags.
            string[] args = ["-s", device.Target, "install-multiple", "-r", "-d", .. splits];
            var install = await runner.RunAsync("adb", args, ct);
            var ok = install.ExitCode == 0 && install.Stdout.Contains("Success", StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                logger.LogWarning("device update: {Id} install failed: {Out}", device.Id, Trim(install.Stdout + install.Stderr));
                return new DeviceUpdateOutcome(true, false, from, targetAppVersion, $"install failed: {Trim(install.Stdout + install.Stderr)}");
            }

            var now = await ReadInstalledAsync(device, ct);
            var verified = now == targetAppVersion;
            logger.LogInformation("device update: {Id} android now {Now} (target {Target}, verified {V})",
                device.Id, now, targetAppVersion, verified);
            return new DeviceUpdateOutcome(true, verified, from, now,
                verified ? "updated" : $"installed but version reads {now}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private async Task<string?> ReadInstalledAsync(Device device, CancellationToken ct)
    {
        var probe = await new AdbDeviceProbe(runner, device.Target, device.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }

    // An XAPK is a zip of split .apk files. Write each .apk entry to dir, return their paths. (Some XAPKs
    // also carry an obb + manifest json; we only install the .apk members.)
    private static List<string> UnzipSplits(byte[] xapk, string dir)
    {
        var paths = new List<string>();
        using var ms = new MemoryStream(xapk, writable: false);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(dir, Path.GetFileName(entry.FullName));
            entry.ExtractToFile(dest, overwrite: true);
            paths.Add(dest);
        }
        return paths;
    }

    private static string Trim(string s) => s.Trim() is { Length: > 200 } t ? t[..200] : s.Trim();
}

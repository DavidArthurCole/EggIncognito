using System.IO.Compression;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Services.Backfill.Sources;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class AndroidDeviceUpdaterTests
{
    sealed class FakeRunner(Func<string[], ProcessResult> fn) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) => Task.FromResult(fn(args));
    }

    sealed class FakeDownloader(byte[]? bytes) : IApkDownloader
    {
        public Task<byte[]?> DownloadApkAsync(string appVersion, CancellationToken ct = default) => Task.FromResult(bytes);
    }

    static Device Dev => new() { Id = "a", Platform = "android", Target = "SER", Package = "com.auxbrain.egginc" };

    // A minimal XAPK: a zip containing two .apk entries (and a non-apk member that must be ignored).
    static byte[] MakeXapk()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] data)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
            Add("base.apk", [1, 2, 3]);
            Add("split_config.arm64_v8a.apk", [4, 5, 6]);
            Add("manifest.json", [7]);
        }
        return ms.ToArray();
    }

    // dumpsys responses: the updater probes installed before + after. Sequence them by call.
    static FakeRunner SeqRunner(string installedBefore, string installedAfter, int installExit, string installOut)
    {
        var dumpsysCalls = 0;
        return new FakeRunner(args =>
        {
            if (args.Contains("dumpsys"))
            {
                var v = dumpsysCalls++ == 0 ? installedBefore : installedAfter;
                return new ProcessResult(0, $"versionCode=1\nversionName={v}\n", "");
            }
            if (args.Contains("install-multiple"))
                return new ProcessResult(installExit, installOut, "");
            return new ProcessResult(0, "", "");
        });
    }

    [Fact]
    public async Task Update_Verified_WhenVersionClimbs()
    {
        var runner = SeqRunner("1.35.7", "1.35.8", 0, "Success\n");
        var u = new AndroidDeviceUpdater(runner, new FakeDownloader(MakeXapk()), NullLogger<AndroidDeviceUpdater>.Instance);
        var o = await u.UpdateAsync(Dev, "1.35.8", default);
        Assert.True(o.Started);
        Assert.True(o.Verified);
        Assert.Equal("1.35.8", o.ToVersion);
    }

    [Fact]
    public async Task Update_NoOp_WhenAlreadyCurrent()
    {
        var runner = SeqRunner("1.35.8", "1.35.8", 0, "Success\n");
        var u = new AndroidDeviceUpdater(runner, new FakeDownloader(MakeXapk()), NullLogger<AndroidDeviceUpdater>.Instance);
        var o = await u.UpdateAsync(Dev, "1.35.8", default);
        Assert.False(o.Started);
        Assert.True(o.Verified);
        Assert.Equal("already current", o.Note);
    }

    [Fact]
    public async Task Update_DownloadFails_NotStarted()
    {
        var runner = SeqRunner("1.35.7", "1.35.7", 0, "Success\n");
        var u = new AndroidDeviceUpdater(runner, new FakeDownloader(null), NullLogger<AndroidDeviceUpdater>.Instance);
        var o = await u.UpdateAsync(Dev, "1.35.8", default);
        Assert.False(o.Started);
        Assert.False(o.Verified);
        Assert.Contains("download", o.Note);
    }

    [Fact]
    public async Task Update_InstallFails_StartedNotVerified()
    {
        var runner = SeqRunner("1.35.7", "1.35.7", 1, "Failure [INSTALL_FAILED]\n");
        var u = new AndroidDeviceUpdater(runner, new FakeDownloader(MakeXapk()), NullLogger<AndroidDeviceUpdater>.Instance);
        var o = await u.UpdateAsync(Dev, "1.35.8", default);
        Assert.True(o.Started);
        Assert.False(o.Verified);
        Assert.Contains("install failed", o.Note);
    }

    [Fact]
    public async Task Update_InstalledButVersionStuck_StartedNotVerified()
    {
        // install reports Success but the version did not climb (a silent no-op / wrong apk).
        var runner = SeqRunner("1.35.7", "1.35.7", 0, "Success\n");
        var u = new AndroidDeviceUpdater(runner, new FakeDownloader(MakeXapk()), NullLogger<AndroidDeviceUpdater>.Instance);
        var o = await u.UpdateAsync(Dev, "1.35.8", default);
        Assert.True(o.Started);
        Assert.False(o.Verified);
    }
}

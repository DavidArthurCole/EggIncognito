using EggIncognito.Core.Services.Devices;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DeviceProbeTests
{
    sealed class FakeRunner(Func<string, string[], ProcessResult> fn) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) => Task.FromResult(fn(exe, args));
    }

    [Fact]
    public async Task Adb_Reachable_ReturnsVersionAndBuild()
    {
        var runner = new FakeRunner((exe, args) =>
        {
            Assert.Equal("adb", exe);
            Assert.Contains("RF8X20GLYDY", args);
            Assert.Contains("com.auxbrain.egginc", args);
            return new ProcessResult(0, "versionCode=111344 minSdk=24\nversionName=1.35.7\n", "");
        });
        var probe = new AdbDeviceProbe(runner, "RF8X20GLYDY", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.True(r.Reachable);
        Assert.Equal("1.35.7", r.InstalledAppVersion);
        Assert.Equal("111344", r.InstalledBuild);
    }

    [Fact]
    public async Task Adb_NoDevice_NotReachable()
    {
        var runner = new FakeRunner((_, _) => new ProcessResult(1, "", "error: device 'RF8X20GLYDY' not found"));
        var probe = new AdbDeviceProbe(runner, "RF8X20GLYDY", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.False(r.Reachable);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public async Task Ios_Reachable_ReturnsAppVersionNullBuild()
    {
        const string csv = "com.auxbrain.egginc, \"1.35.8\", \"Egg, Inc.\"\n";
        var runner = new FakeRunner((exe, args) =>
        {
            Assert.Equal("ideviceinstaller", exe);
            Assert.Contains("3489c6b0", args);
            Assert.Contains("list", args);
            return new ProcessResult(0, csv, "");
        });
        var probe = new IosDeviceProbe(runner, "3489c6b0", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.True(r.Reachable);
        Assert.Equal("1.35.8", r.InstalledAppVersion);
        Assert.Null(r.InstalledBuild);
    }

    [Fact]
    public async Task Ios_AppNotInstalled_ReachableButNoVersion()
    {
        const string csv = "com.WA5H2B7E4G.com.rileytestut.AltStore, \"48\", \"AltStore\"\n";
        var runner = new FakeRunner((_, _) => new ProcessResult(0, csv, ""));
        var probe = new IosDeviceProbe(runner, "3489c6b0", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.True(r.Reachable);     // device answered
        Assert.Null(r.InstalledAppVersion);
        Assert.NotNull(r.Note);       // "app not installed"
    }

    [Fact]
    public async Task Ios_ToolMissing_NotReachable()
    {
        var runner = new FakeRunner((_, _) => new ProcessResult(-1, "", "ideviceinstaller: not found"));
        var probe = new IosDeviceProbe(runner, "3489c6b0", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.False(r.Reachable);
    }
}

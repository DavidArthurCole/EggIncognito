using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceCaptureTests {
    private static string TempDir() {
        string d = Path.Combine(Path.GetTempPath(), "egi-rinfo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Rinfo_Observe_Then_Latest_RoundTrips_PerDevice() {
        string dir = TempDir();
        try {
            var store = new DeviceRinfoStore(dir);
            store.Observe("dev-a", new RinfoHarvester.ObservedVersion("IOS", "1.36", "111350", 72),
                "2026-01-01T00:00:00Z");
            store.Observe("dev-b", new RinfoHarvester.ObservedVersion("ANDROID", "1.35.7", "111344", 72),
                "2026-01-01T00:00:00Z");

            var a = store.Latest("dev-a");
            var b = store.Latest("dev-b");
            Assert.Equal("111350", a!.Build);
            Assert.Equal("ios", a.Platform);
            Assert.Equal("111344", b!.Build);
            Assert.Equal("android", b.Platform);
            Assert.Null(store.Latest("nope"));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Rinfo_Observe_KeepsPriorNonNull_OnThinnerObservation() {
        string dir = TempDir();
        try {
            var store = new DeviceRinfoStore(dir);
            store.Observe("d", new RinfoHarvester.ObservedVersion("IOS", "1.36", "111350", 72), "t1");
            store.Observe("d", new RinfoHarvester.ObservedVersion("IOS", null, null, null), "t2");

            var v = store.Latest("d")!;
            Assert.Equal("111350", v.Build);
            Assert.Equal("1.36", v.Version);
            Assert.Equal(72, v.ClientVersion);
            Assert.Equal("t2", v.LastSeen);
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Rinfo_CorruptFile_ReadsEmpty() {
        string dir = TempDir();
        try {
            File.WriteAllText(Path.Combine(dir, "device-rinfo.json"), "{ not json ]");
            var store = new DeviceRinfoStore(dir);
            Assert.Empty(store.Load());
            Assert.Null(store.Latest("d"));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void HostAddress_Picks_Private_Over_Public() {
        var nics = new List<HostAddress.Nic> {
            new("eth-pub", true, false, ["8.8.4.4"]),
            new("eth-lan", true, false, ["192.168.1.50"])
        };
        Assert.Equal("192.168.1.50", HostAddress.Pick(nics));
    }

    [Fact]
    public void HostAddress_Skips_Down_Loopback_Virtual_Apipa() {
        var nics = new List<HostAddress.Nic> {
            new("lo", true, true, ["127.0.0.1"]),
            new("docker0", true, false, ["172.17.0.1"]),
            new("eth-down", false, false, ["192.168.1.9"]),
            new("wlan0", true, false, ["169.254.1.2"]),
            new("eth0", true, false, ["10.0.0.5"])
        };
        Assert.Equal("10.0.0.5", HostAddress.Pick(nics));
    }

    [Fact]
    public void HostAddress_Resolve_ConfigOverride_Wins() {
        var nics = new List<HostAddress.Nic> { new("eth0", true, false, ["10.0.0.5"]) };
        Assert.Equal("203.0.113.7", HostAddress.Resolve("203.0.113.7", nics));
    }

    [Fact]
    public void HostAddress_Pick_NoCandidates_ReturnsNull() =>
        Assert.Null(HostAddress.Pick([]));

    [Fact]
    public async Task Adb_SetProxy_EmitsSettingsPut() {
        var runner = new CapturingRunner(_ => new ProcessResult(0, "", ""));
        var cfg = new AdbProxyConfigurator(runner);
        (bool ok, _) =
            await cfg.SetProxyAsync(new DeviceTarget("a", "android", "SERIAL", "com.auxbrain.egginc"), "10.0.0.5", 9100, default);
        Assert.True(ok);
        string[] call = runner.Calls[0];
        Assert.Equal("adb", call[0]);
        Assert.Contains("SERIAL", call);
        Assert.Contains("http_proxy", call);
        Assert.Contains("10.0.0.5:9100", call);
    }

    [Fact]
    public async Task Adb_ClearProxy_EmitsSentinel() {
        var runner = new CapturingRunner(_ => new ProcessResult(0, "", ""));
        var cfg = new AdbProxyConfigurator(runner);
        (bool ok, _) = await cfg.ClearProxyAsync(new DeviceTarget("a", "android", "SERIAL", "com.auxbrain.egginc"), default);
        Assert.True(ok);
        Assert.Contains(":0", runner.Calls[0]);
    }

    [Fact]
    public async Task Adb_NonZeroExit_ReturnsNote() {
        var runner = new CapturingRunner(_ => new ProcessResult(1, "", "device offline"));
        var cfg = new AdbProxyConfigurator(runner);
        (bool ok, string? note) =
            await cfg.SetProxyAsync(new DeviceTarget("a", "android", "SERIAL", "com.auxbrain.egginc"), "10.0.0.5", 9100, default);
        Assert.False(ok);
        Assert.Contains("device offline", note);
    }

    [Fact]
    public async Task Ios_SetProxy_FillsTemplatePlaceholders() {
        var runner = new CapturingRunner(_ => new ProcessResult(0, "", ""));
        var ssh = new IosProxyConfigurator.SshConfig("1.2.3.4", "2222", "/k",
            "set-proxy {host} {port}", "clear-proxy");
        var cfg = new IosProxyConfigurator(runner, ssh);
        (bool ok, _) = await cfg.SetProxyAsync(new DeviceTarget("i", "ios", "UDID", "com.auxbrain.egginc"), "10.0.0.5", 9101, default);
        Assert.True(ok);
        string remote = runner.Calls[0].Last();
        Assert.Equal("set-proxy 10.0.0.5 9101", remote);
    }

    [Fact]
    public async Task Ios_SetProxy_NoTemplate_FailsClosed() {
        var runner = new CapturingRunner(_ => new ProcessResult(0, "", ""));
        var ssh = new IosProxyConfigurator.SshConfig("1.2.3.4", "2222", "/k", null, null);
        var cfg = new IosProxyConfigurator(runner, ssh);
        (bool ok, string? note) =
            await cfg.SetProxyAsync(new DeviceTarget("i", "ios", "UDID", "com.auxbrain.egginc"), "10.0.0.5", 9101, default);
        Assert.False(ok);
        Assert.Empty(runner.Calls);
        Assert.Contains("guid", note);
    }

    [Fact]
    public async Task Ios_SetProxy_NoSshCreds_FailsClosed() {
        var runner = new CapturingRunner(_ => new ProcessResult(0, "", ""));
        var ssh = new IosProxyConfigurator.SshConfig(null, "2222", null,
            "set {host} {port}", "clear");
        var cfg = new IosProxyConfigurator(runner, ssh);
        (bool ok, _) = await cfg.SetProxyAsync(new DeviceTarget("i", "ios", "UDID", "com.auxbrain.egginc"), "10.0.0.5", 9101, default);
        Assert.False(ok);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void CapturePorts_DevicesGet_NonOverlapping_Blocks() {
        const int basePort = 9100;
        int d0 = DeviceCaptureManager.PortForIndex(basePort, 0);
        int d1 = DeviceCaptureManager.PortForIndex(basePort, 1);
        Assert.Equal(9100, d0);
        Assert.Equal(9103, d1);

        var block0 = Enumerable.Range(d0, DeviceCaptureManager.PortsPerDevice);
        var block1 = Enumerable.Range(d1, DeviceCaptureManager.PortsPerDevice);
        Assert.Empty(block0.Intersect(block1));
        Assert.True(DeviceCaptureManager.PortsPerDevice >= 3, "each proxy binds 3 ports");
    }

    [Fact]
    public void Platform_Identifiers_AreStable() {
        Assert.Equal("android",
            new AdbProxyConfigurator(new CapturingRunner(_ => new ProcessResult(0, "", ""))).Platform);
        Assert.Equal("ios", new IosProxyConfigurator(
            new CapturingRunner(_ => new ProcessResult(0, "", "")),
            new IosProxyConfigurator.SshConfig(null, "2222", null, null, null)).Platform);
    }

    [Fact]
    public void IosProxy_BuildsSetCommand_FromGuid_WhenNoTemplate() {
        var cfg = new IosProxyConfigurator.SshConfig(
            "10.0.0.1", "2222", "/k", null, null, "GUID-123");
        var proxy = new IosProxyConfigurator(new CapturingRunner(_ => new ProcessResult(0, "", "")), cfg);

        string set = proxy.BuildSet("192.168.1.5", 9100);
        Assert.Contains("/cores/binpack/usr/bin/plutil", set);
        Assert.Contains("-key NetworkServices -key GUID-123 -key Proxies", set);
        Assert.Contains("-key HTTPProxy -value 192.168.1.5 -type string", set);
        Assert.Contains("-key HTTPSPort -value 9100 -type int", set);
        Assert.Equal(6, set.Split(';').Length);

        string clear = proxy.BuildClear();
        Assert.Contains("-key HTTPEnable -value 0 -type int", clear);
        Assert.Contains("-key HTTPSEnable -value 0 -type int", clear);
    }

    [Fact]
    public async Task IosProxy_TemplateOverride_WinsOverBuilt() {
        string? seen = null;
        var runner = new CapturingRunner(args => {
            seen = args.LastOrDefault();
            return new ProcessResult(0, "", "");
        });
        var cfg = new IosProxyConfigurator.SshConfig(
            "10.0.0.1", "2222", "/k", "echo {host}:{port}", "echo clear", "GUID-123");
        var proxy = new IosProxyConfigurator(runner, cfg);

        (bool ok, _) = await proxy.SetProxyAsync(new DeviceTarget("d", "ios", "udid", "com.auxbrain.egginc"), "1.2.3.4", 80, default);
        Assert.True(ok);
        Assert.Equal("echo 1.2.3.4:80", seen);
    }

    private sealed class CapturingRunner(Func<string[], ProcessResult> fn) : IProcessRunner {
        public readonly List<string[]> Calls = [];

        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
            Calls.Add([exe, .. args]);
            return Task.FromResult(fn(args));
        }
    }
}

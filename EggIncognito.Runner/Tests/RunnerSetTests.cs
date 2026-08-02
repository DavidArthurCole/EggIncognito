using EggIncognito.Core.Services.Devices;
using EggIncognito.Runner.Devices;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Runners;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class RunnerSetTests {
    private static RunnerDeps Deps() {
        var stash = Path.Combine(Path.GetTempPath(), $"stash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stash);
        return new RunnerDeps(new StubProto(), new NullClientVersionReader(), stash,
            Path.Combine(stash, "ios-binary"), null, "com.auxbrain.egginc", _ => { });
    }

    [Fact]
    public void Build_FromDeviceFiles_KeysById() {
        var devices = new List<DeviceFileParser.ParsedDevice> {
            new(1, "pixel", "android", "Pixel", "127.0.0.1:5555", null),
            new(2, "iphone", "ios", "iPhone", null, null),
        };
        var set = RunnerSet.Build(devices, Deps(), () => null);
        Assert.Equal(2, set.Runners.Count);
        Assert.True(set.ById.ContainsKey("pixel"));
        Assert.True(set.ById.ContainsKey("iphone"));
    }

    [Fact]
    public void Build_NoDevices_UsesLegacyFallbackKeyedByPlatform() {
        IDeviceRunner legacy = new FakeRunner("android");
        var set = RunnerSet.Build([], Deps(), () => legacy);
        Assert.Single(set.Runners);
        Assert.Same(legacy, set.ById["android"]);
    }

    [Fact]
    public void Build_NoDevicesNoFallback_IsEmpty() {
        var set = RunnerSet.Build([], Deps(), () => null);
        Assert.Empty(set.Runners);
        Assert.Empty(set.ById);
    }

    [Fact]
    public void Build_SkipsUnbuildableDevices() {
        var devices = new List<DeviceFileParser.ParsedDevice> {
            new(1, "pixel", "android", "Pixel", "127.0.0.1:5555", null),
            new(2, "broken", "android", "Broken", null, null),
        };
        var set = RunnerSet.Build(devices, Deps(), () => null);
        Assert.Single(set.Runners);
        Assert.True(set.ById.ContainsKey("pixel"));
    }

    private sealed class StubProto : EggIncognito.Runner.Extract.IProtoExtractor {
        public EggIncognito.Runner.Extract.ProtoExtraction Extract(string apkPath) => new([], "");
    }

    private sealed class FakeRunner(string platform) : IDeviceRunner {
        public string Platform => platform;
        public RunOutcome RunOnce(bool force) => new(false, null, null, "fake");
    }
}

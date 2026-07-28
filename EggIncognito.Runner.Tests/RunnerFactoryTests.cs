using EggIncognito.Core.Services.Devices;
using EggIncognito.Runner.Devices;
using EggIncognito.Runner.Extract;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class RunnerFactoryTests {
    private static RunnerDeps Deps() {
        var stash = Path.Combine(Path.GetTempPath(), $"stash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stash);
        return new RunnerDeps(
            Proto: new StubProtoExtractor(),
            ClientVersion: new NullClientVersionReader(),
            ApkStashDir: stash,
            IosBinaryPath: Path.Combine(stash, "ios-binary"),
            PrevClientVersion: null,
            DefaultPackage: "com.auxbrain.egginc",
            OnNewVersion: _ => { });
    }

    [Fact]
    public void Build_Android_ReturnsAndroidRunner() {
        var d = new DeviceFileParser.ParsedDevice(1, "pixel", "android", "Pixel", "127.0.0.1:5555", null);
        var runner = RunnerFactory.Build(d, Deps());
        Assert.NotNull(runner);
        Assert.Equal("android", runner!.Platform);
    }

    [Fact]
    public void Build_Ios_ReturnsIosRunner() {
        var d = new DeviceFileParser.ParsedDevice(2, "iphone", "ios", "iPhone", null, null);
        var runner = RunnerFactory.Build(d, Deps());
        Assert.NotNull(runner);
        Assert.Equal("ios", runner!.Platform);
    }

    [Fact]
    public void Build_AndroidWithoutTarget_ReturnsNull() {
        var d = new DeviceFileParser.ParsedDevice(1, "pixel", "android", "Pixel", null, null);
        Assert.Null(RunnerFactory.Build(d, Deps()));
    }

    [Fact]
    public void Build_UnknownPlatform_ReturnsNull() {
        var d = new DeviceFileParser.ParsedDevice(1, "thing", "symbian", "Thing", "x", null);
        Assert.Null(RunnerFactory.Build(d, Deps()));
    }

    [Fact]
    public void Build_MissingId_ReturnsNull() {
        var d = new DeviceFileParser.ParsedDevice(1, null, "android", "x", "127.0.0.1:5555", null);
        Assert.Null(RunnerFactory.Build(d, Deps()));
    }

    private sealed class StubProtoExtractor : IProtoExtractor {
        public byte[] Extract(string apkPath) => [];
    }
}

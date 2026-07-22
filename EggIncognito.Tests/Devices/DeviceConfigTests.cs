using EggIncognito.Services.Devices;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests.Devices;

public class DeviceConfigTests {
    private static IConfiguration Cfg(Dictionary<string, string?> d) =>
        new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    [Fact]
    public void Bind_Empty_NoDevicesDefaultsOn() {
        var c = DeviceConfig.Bind(Cfg([]));
        Assert.True(c.Enabled);
        Assert.Equal(30, c.IntervalMinutes);
        Assert.Empty(c.Devices);
    }

    [Fact]
    public void Bind_TwoDevices_ParsesAll() {
        var c = DeviceConfig.Bind(Cfg(new() {
            ["DevicePolling:Enabled"] = "true",
            ["DevicePolling:IntervalMinutes"] = "15",
            ["Devices:0:Id"] = "frame-android",
            ["Devices:0:Platform"] = "android",
            ["Devices:0:Label"] = "A15",
            ["Devices:0:Target"] = "RF8X20GLYDY",
            ["Devices:0:Package"] = "com.auxbrain.egginc",
            ["Devices:1:Id"] = "frame-iphone",
            ["Devices:1:Platform"] = "ios",
            ["Devices:1:Label"] = "iPhone 8",
            ["Devices:1:Target"] = "3489c6b0",
        }));
        Assert.Equal(15, c.IntervalMinutes);
        Assert.Equal(2, c.Devices.Count);
        Assert.Equal("frame-android", c.Devices[0].Id);
        Assert.Equal("RF8X20GLYDY", c.Devices[0].Target);
        Assert.Equal("com.auxbrain.egginc", c.Devices[1].Package);
    }

    [Fact]
    public void Bind_DropsEntriesMissingIdOrTarget() {
        var c = DeviceConfig.Bind(Cfg(new() {
            ["Devices:0:Platform"] = "android",
            ["Devices:1:Id"] = "ok",
            ["Devices:1:Platform"] = "ios",
            ["Devices:1:Target"] = "udid",
        }));
        Assert.Single(c.Devices);
        Assert.Equal("ok", c.Devices[0].Id);
    }

    [Fact]
    public void Bind_ReadsDeviceFilesFromDirInIndexOrder() {
        using var tmp = new TempDir();
        tmp.Write("ios.egidevice.1", "Id=frame-iphone\nPlatform=ios\nLabel=iPhone 8\nTarget=udid");
        tmp.Write("android.egidevice.0", "Id=frame-android\nPlatform=android\nLabel=A15\nTarget=RF8X20GLYDY");
        tmp.Write("runner-android.env", "Id=nope\nTarget=nope");

        var c = DeviceConfig.Bind(Cfg(new() { ["Devices:Dir"] = tmp.Path }));

        Assert.Equal(2, c.Devices.Count);
        Assert.Equal("frame-android", c.Devices[0].Id);
        Assert.Equal("frame-iphone", c.Devices[1].Id);
        Assert.Equal("com.auxbrain.egginc", c.Devices[0].Package);
    }

    [Fact]
    public void Bind_MissingDirFallsBackToInline() {
        var c = DeviceConfig.Bind(Cfg(new() {
            ["Devices:Dir"] = "/no/such/dir",
            ["Devices:0:Id"] = "inline",
            ["Devices:0:Target"] = "t",
        }));
        Assert.Single(c.Devices);
        Assert.Equal("inline", c.Devices[0].Id);
    }

    [Fact]
    public void Bind_InlineOverridesDirOnIdCollision() {
        using var tmp = new TempDir();
        tmp.Write("android.egidevice.0", "Id=dev\nPlatform=android\nTarget=from-file");

        var c = DeviceConfig.Bind(Cfg(new() {
            ["Devices:Dir"] = tmp.Path,
            ["Devices:0:Id"] = "dev",
            ["Devices:0:Target"] = "from-inline",
        }));

        Assert.Single(c.Devices);
        Assert.Equal("from-inline", c.Devices[0].Target);
    }

    [Fact]
    public void Bind_DirAndInlineMergeDistinctIds() {
        using var tmp = new TempDir();
        tmp.Write("android.egidevice.0", "Id=from-file\nTarget=t");

        var c = DeviceConfig.Bind(Cfg(new() {
            ["Devices:Dir"] = tmp.Path,
            ["Devices:0:Id"] = "from-inline",
            ["Devices:0:Target"] = "t",
        }));

        Assert.Equal(2, c.Devices.Count);
        Assert.Contains(c.Devices, d => d.Id == "from-file");
        Assert.Contains(c.Devices, d => d.Id == "from-inline");
    }

    private sealed class TempDir : IDisposable {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "egi-dev-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Write(string name, string content) => File.WriteAllText(System.IO.Path.Combine(Path, name), content);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}

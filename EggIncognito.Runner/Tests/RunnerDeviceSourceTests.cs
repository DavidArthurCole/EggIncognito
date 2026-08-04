using EggIncognito.Runner.Devices;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class RunnerDeviceSourceTests {
    [Fact]
    public void Read_NullOrMissingDir_ReturnsEmpty() {
        using var tmp = new TempDir();
        Assert.Empty(RunnerDeviceSource.Read(null));
        Assert.Empty(RunnerDeviceSource.Read(tmp.Combine("nope")));
    }

    [Fact]
    public void Read_ParsesAndOrdersDeviceFiles() {
        using var tmp = new TempDir();
        tmp.Write("ios.egidevice.2", "Id=iphone\nPlatform=ios\n");
        tmp.Write("android.egidevice.1", "Id=pixel\nTarget=127.0.0.1:5555\n");
        tmp.Write("notes.txt", "ignore me");

        var devices = RunnerDeviceSource.Read(tmp.Path);

        Assert.Equal(2, devices.Count);
        Assert.Equal("pixel", devices[0].Id);
        Assert.Equal("android", devices[0].Platform);
        Assert.Equal("iphone", devices[1].Id);
        Assert.Equal("ios", devices[1].Platform);
    }
}

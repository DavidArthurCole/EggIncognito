using EggIncognito.Runner.Devices;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class RunnerDeviceSourceTests {
    [Fact]
    public void Read_NullOrMissingDir_ReturnsEmpty() {
        Assert.Empty(RunnerDeviceSource.Read(null));
        Assert.Empty(RunnerDeviceSource.Read(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}")));
    }

    [Fact]
    public void Read_ParsesAndOrdersDeviceFiles() {
        var dir = Path.Combine(Path.GetTempPath(), $"egidev-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try {
            File.WriteAllText(Path.Combine(dir, "ios.egidevice.2"), "Id=iphone\nPlatform=ios\n");
            File.WriteAllText(Path.Combine(dir, "android.egidevice.1"), "Id=pixel\nTarget=127.0.0.1:5555\n");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "ignore me");

            var devices = RunnerDeviceSource.Read(dir);

            Assert.Equal(2, devices.Count);
            Assert.Equal("pixel", devices[0].Id);
            Assert.Equal("android", devices[0].Platform);
            Assert.Equal("iphone", devices[1].Id);
            Assert.Equal("ios", devices[1].Platform);
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }
}

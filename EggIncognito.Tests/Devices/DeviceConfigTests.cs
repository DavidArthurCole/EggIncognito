using EggIncognito.Services.Devices;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DeviceConfigTests
{
    static IConfiguration Cfg(Dictionary<string, string?> d) =>
        new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    [Fact]
    public void Bind_Empty_NoDevicesDefaultsOn()
    {
        var c = DeviceConfig.Bind(Cfg(new()));
        Assert.True(c.Enabled);
        Assert.Equal(30, c.IntervalMinutes);
        Assert.Empty(c.Devices);
    }

    [Fact]
    public void Bind_TwoDevices_ParsesAll()
    {
        var c = DeviceConfig.Bind(Cfg(new()
        {
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
    public void Bind_DropsEntriesMissingIdOrTarget()
    {
        var c = DeviceConfig.Bind(Cfg(new()
        {
            ["Devices:0:Platform"] = "android",
            ["Devices:1:Id"] = "ok",
            ["Devices:1:Platform"] = "ios",
            ["Devices:1:Target"] = "udid",
        }));
        Assert.Single(c.Devices);
        Assert.Equal("ok", c.Devices[0].Id);
    }
}

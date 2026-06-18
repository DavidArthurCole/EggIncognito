using EggIncognito.Services.Devices;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DeviceUpdateConfigTests
{
    static IConfiguration Cfg(Dictionary<string, string?> d) =>
        new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    [Fact]
    public void Bind_Default_AllOff()
    {
        var c = DeviceUpdateConfig.Bind(Cfg(new()));
        Assert.False(c.Enabled);
        Assert.False(c.Android);
        Assert.False(c.Ios);
        Assert.False(c.EnabledFor("android"));
    }

    [Fact]
    public void Bind_AndroidOnIosOff()
    {
        var c = DeviceUpdateConfig.Bind(Cfg(new()
        {
            ["DeviceUpdate:Enabled"] = "true",
            ["DeviceUpdate:Android:Enabled"] = "true",
            ["DeviceUpdate:Ios:Enabled"] = "false",
        }));
        Assert.True(c.Enabled);
        Assert.True(c.EnabledFor("android"));
        Assert.False(c.EnabledFor("ios"));
        Assert.False(c.EnabledFor("other"));
    }
}

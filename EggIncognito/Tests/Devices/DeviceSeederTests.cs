using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Tests.Devices;

public class DeviceSeederTests {
    private static Device Row(string id, string origin, bool enabled = true) =>
        new() { Id = id, Origin = origin, Enabled = enabled };

    [Fact]
    public void IsStale_RuntimeOriginRow_SurvivesEvenWhenNotDeclared() =>
        Assert.False(DeviceSeeder.IsStale(Row("provisioned-1", "runtime"), new HashSet<string> { "frame-android" }));

    [Fact]
    public void IsStale_ConfigOriginRowNoLongerDeclared_IsStale() =>
        Assert.True(DeviceSeeder.IsStale(Row("retired-ios", "config"), new HashSet<string> { "frame-android" }));

    [Fact]
    public void IsStale_ConfigOriginRowStillDeclared_NotStale() =>
        Assert.False(DeviceSeeder.IsStale(Row("frame-android", "config"), new HashSet<string> { "frame-android" }));

    [Fact]
    public void IsStale_AlreadyDisabledRow_NotStale() =>
        Assert.False(DeviceSeeder.IsStale(Row("retired-ios", "config", false), new HashSet<string>()));
}

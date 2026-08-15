using EggIncognito.Services.Devices;
using EggIncognito.Services.Devices.Fake;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests.Devices;

public class FakeDeviceOptionsTests {
    private static IConfiguration Cfg(Dictionary<string, string?> d) =>
        new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    [Fact]
    public void Bind_NoDeclarations_YieldsOneIosAndOneAndroidHealthyDevice() {
        var s = FakeDeviceOptions.Bind(Cfg([]));
        Assert.Equal(2, s.Devices.Count);
        Assert.Contains(s.Devices, d => d.Id == "fake-ios-0" && d.Scenario == FakeScenarios.Healthy);
        Assert.Contains(s.Devices, d => d.Id == "fake-android-0" && d.Scenario == FakeScenarios.Healthy);
    }

    [Fact]
    public void Bind_TimingDefaults() {
        var s = FakeDeviceOptions.Bind(Cfg([]));
        Assert.Equal(15, s.SweepMinutes);
        Assert.Equal(45000, s.SlowEntryMs);
        Assert.Equal(35, s.WedgeBackdateMinutes);
        Assert.Equal(4000, s.CaptureIntervalMs);
    }

    [Fact]
    public void Bind_TimingOverrides() {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:SweepMinutes"] = "3",
            ["Devices:Fake:SlowEntryMs"] = "1500",
            ["Devices:Fake:WedgeBackdateMinutes"] = "99",
            ["Devices:Fake:CaptureIntervalMs"] = "800"
        }));
        Assert.Equal(3, s.SweepMinutes);
        Assert.Equal(1500, s.SlowEntryMs);
        Assert.Equal(99, s.WedgeBackdateMinutes);
        Assert.Equal(800, s.CaptureIntervalMs);
    }

    [Theory]
    [InlineData("healthy")]
    [InlineData("store-ahead")]
    [InlineData("slow-harvest")]
    [InlineData("failing-entry")]
    [InlineData("unreachable")]
    [InlineData("wedged")]
    public void Bind_ParsesEveryScenarioName(string scenario) {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Devices:0:Platform"] = "ios",
            ["Devices:Fake:Devices:0:Scenario"] = scenario
        }));
        Assert.Single(s.Devices);
        Assert.Equal(scenario, s.Devices[0].Scenario);
        Assert.Equal($"fake:{scenario}", s.Devices[0].Target);
    }

    [Fact]
    public void Bind_UnknownScenarioFallsBackToHealthy() {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Devices:0:Platform"] = "ios",
            ["Devices:Fake:Devices:0:Scenario"] = "explode"
        }));
        Assert.Equal(FakeScenarios.Healthy, s.Devices[0].Scenario);
    }

    [Fact]
    public void Bind_IdsAreGeneratedAndNeverTakenFromConfig() {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Devices:0:Platform"] = "ios",
            ["Devices:Fake:Devices:0:Id"] = "frame-iphone",
            ["Devices:Fake:Devices:1:Platform"] = "ios",
            ["Devices:Fake:Devices:1:Id"] = "frame-iphone-2",
            ["Devices:Fake:Devices:2:Platform"] = "android",
            ["Devices:Fake:Devices:2:Id"] = "frame-android"
        }));
        Assert.Equal(new[] { "fake-ios-0", "fake-ios-1", "fake-android-0" }, s.Devices.Select(d => d.Id));
        Assert.DoesNotContain(s.Devices, d => d.Id.Contains("frame", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bind_UnknownPlatformBecomesAndroid() {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Devices:0:Platform"] = "windows-phone"
        }));
        Assert.Equal("android", s.Devices[0].Platform);
        Assert.Equal("fake-android-0", s.Devices[0].Id);
    }

    [Fact]
    public void Bind_LabelsAlwaysCarryTheWordFake() {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Devices:0:Platform"] = "ios",
            ["Devices:Fake:Devices:0:Label"] = "iPhone 8",
            ["Devices:Fake:Devices:1:Platform"] = "android",
            ["Devices:Fake:Devices:1:Label"] = "my FAKE pixel"
        }));
        Assert.All(s.Devices, d => Assert.Contains("fake", d.Label, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("fake iPhone 8", s.Devices[0].Label);
        Assert.Equal("my FAKE pixel", s.Devices[1].Label);
    }

    [Fact]
    public void Bind_PackageDefaultsToTheGamePackage() {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Devices:0:Platform"] = "android"
        }));
        Assert.Equal(FakeDeviceOptions.DefaultPackage, s.Devices[0].Package);
    }

    [Fact]
    public void Bind_ReadsVersionKnobs() {
        var s = FakeDeviceOptions.Bind(Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Devices:0:Platform"] = "ios",
            ["Devices:Fake:Devices:0:AppVersion"] = "1.37.1",
            ["Devices:Fake:Devices:0:Build"] = "1140823",
            ["Devices:Fake:Devices:0:ClientVersion"] = "73",
            ["Devices:Fake:Devices:0:ProbeDelayMs"] = "25"
        }));
        Assert.Equal("1.37.1", s.Devices[0].AppVersion);
        Assert.Equal("1140823", s.Devices[0].Build);
        Assert.Equal(73, s.Devices[0].ClientVersion);
        Assert.Equal(25, s.Devices[0].ProbeDelayMs);
    }

    [Fact]
    public void Entries_MirrorTheFakeDeviceList() {
        var s = FakeDeviceOptions.Bind(Cfg([]));
        var entries = FakeDeviceOptions.Entries(s);
        Assert.Equal(s.Devices.Select(d => d.Id), entries.Select(e => e.Id));
        Assert.All(entries, e => Assert.StartsWith(FakeDeviceOptions.TargetPrefix, e.Target, StringComparison.Ordinal));
    }

    [Fact]
    public void FakeSection_IsInvisibleToTheNormalDeviceBinder() {
        var config = Cfg(new Dictionary<string, string?> {
            ["Devices:Fake:Enabled"] = "true",
            ["Devices:Fake:Devices:0:Platform"] = "ios",
            ["Devices:Fake:Devices:0:Target"] = "should-never-be-read"
        });
        Assert.Empty(DeviceConfig.Bind(config).Devices);
    }

    [Fact]
    public void Settings_ForResolvesByIdCaseInsensitively() {
        var s = FakeDeviceOptions.Bind(Cfg([]));
        Assert.NotNull(s.For("FAKE-IOS-0"));
        Assert.Null(s.For("frame-iphone"));
    }
}

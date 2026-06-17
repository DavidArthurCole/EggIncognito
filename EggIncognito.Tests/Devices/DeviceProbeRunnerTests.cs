using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Services.Devices;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DeviceProbeRunnerTests
{
    static Device Android => new() { Id = "a", Platform = "android", Target = "s", Package = "p" };
    static Device Ios => new() { Id = "i", Platform = "ios", Target = "u", Package = "p" };

    [Fact]
    public void Classify_Unreachable()
    {
        var r = new DeviceProbeResult(false, null, null, "off");
        Assert.Equal("unreachable", DeviceProbeRunner.Classify(Android, r, "111344", "1.35.7"));
    }

    [Fact]
    public void Classify_Android_InstalledBuildAhead_NewVersion()
    {
        var r = new DeviceProbeResult(true, "1.35.7", "111344", null);
        Assert.Equal("new_version", DeviceProbeRunner.Classify(Android, r, "111340", "1.35.6"));
    }

    [Fact]
    public void Classify_Android_BuildEqual_NoChange()
    {
        var r = new DeviceProbeResult(true, "1.35.7", "111344", null);
        Assert.Equal("no_change", DeviceProbeRunner.Classify(Android, r, "111344", "1.35.7"));
    }

    [Fact]
    public void Classify_Android_NothingExtractedYet_NewVersion()
    {
        var r = new DeviceProbeResult(true, "1.35.7", "111344", null);
        Assert.Equal("new_version", DeviceProbeRunner.Classify(Android, r, null, null));
    }

    [Fact]
    public void Classify_Ios_InstalledSemverAhead_NewVersion()
    {
        var r = new DeviceProbeResult(true, "1.35.8", null, null);
        Assert.Equal("new_version", DeviceProbeRunner.Classify(Ios, r, null, "1.35.7"));
    }

    [Fact]
    public void Classify_Ios_SameSemver_NoChange()
    {
        var r = new DeviceProbeResult(true, "1.35.8", null, null);
        Assert.Equal("no_change", DeviceProbeRunner.Classify(Ios, r, null, "1.35.8"));
    }

    [Fact]
    public void Classify_ReachableButNoVersion_Error()
    {
        var r = new DeviceProbeResult(true, null, null, "app not installed");
        Assert.Equal("error", DeviceProbeRunner.Classify(Ios, r, null, "1.35.8"));
    }

    [Fact]
    public void ProbeFor_PicksPlatformProbe()
    {
        Assert.IsType<AdbDeviceProbe>(DeviceProbeRunner.ProbeFor(Android, new ProcessRunner()));
        Assert.IsType<IosDeviceProbe>(DeviceProbeRunner.ProbeFor(Ios, new ProcessRunner()));
    }
}

using EggIncognito.Core.Services.Devices;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DeviceParsingTests
{
    // Real dumpsys excerpt captured from frame's A15 on 2026-06-17.
    const string Dumpsys = "    versionCode=111344 minSdk=24 targetSdk=36\n    versionName=1.35.7\n";

    [Fact]
    public void AndroidVersion_ParsesNameAndCode()
    {
        var (app, build) = DeviceParsing.AndroidVersion(Dumpsys);
        Assert.Equal("1.35.7", app);
        Assert.Equal("111344", build);
    }

    [Fact]
    public void AndroidVersion_NoMatch_ReturnsNulls()
    {
        var (app, build) = DeviceParsing.AndroidVersion("garbage");
        Assert.Null(app);
        Assert.Null(build);
    }

    // Real `ideviceinstaller list` CSV captured from a container on frame (Debian-packaged build,
    // no -o xml): one line per app, `<bundleId>, "<shortVersion>", "<displayName>"`.
    const string ListCsv =
        "com.WA5H2B7E4G.com.rileytestut.AltStore, \"48\", \"AltStore\"\n" +
        "com.auxbrain.egginc, \"1.35.8\", \"Egg, Inc.\"\n";

    [Fact]
    public void IosAppVersion_FindsShortVersion()
    {
        Assert.Equal("1.35.8", DeviceParsing.IosAppVersion(ListCsv, "com.auxbrain.egginc"));
    }

    [Fact]
    public void IosAppVersion_BundleNotInstalled_ReturnsNull()
    {
        Assert.Null(DeviceParsing.IosAppVersion(ListCsv, "com.does.not.exist"));
    }

    [Fact]
    public void IosAppVersion_Garbage_ReturnsNull()
    {
        Assert.Null(DeviceParsing.IosAppVersion("not a list", "com.auxbrain.egginc"));
    }
}

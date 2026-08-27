using EggIncognito.Core.Services.Devices;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class AdbParseTests {
    [Fact]
    public void ApkPaths_ExtractsPathAfterColon() {
        var pm = "package:/data/app/~~a==/com.auxbrain.egginc-b==/base.apk\n"
               + "package:/data/app/~~a==/com.auxbrain.egginc-b==/split_config.arm64_v8a.apk\n";
        var paths = DeviceParsing.ApkPaths(pm);
        Assert.Equal(2, paths.Count);
        Assert.Contains("/base.apk", paths[0]);
        Assert.EndsWith("split_config.arm64_v8a.apk", paths[1]);
    }

    [Fact]
    public void SelectArmSplit_PicksArmSplit() {
        var pm = "package:/data/app/x/base.apk\n"
               + "package:/data/app/x/split_config.arm64_v8a.apk\n"
               + "package:/data/app/x/split_config.en.apk\n";
        Assert.EndsWith("arm64_v8a.apk", DeviceParsing.SelectArmSplit(pm));
    }

    [Fact]
    public void SelectArmSplit_NoneReturnsNull() => Assert.Null(DeviceParsing.SelectArmSplit("package:/data/app/x/base.apk\n"));
}

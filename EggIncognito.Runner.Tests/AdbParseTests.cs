using EggIncognito.Runner.Adb;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class AdbParseTests
{
    [Fact]
    public void ParseVersionName_PullsVersionNameToken()
    {
        var dumpsys = "    versionCode=111343 minSdk=24 targetSdk=34\n    versionName=1.35.7\n";
        Assert.Equal("1.35.7", AdbClient.ParseVersionName(dumpsys));
    }

    [Fact]
    public void ParseVersionCode_PullsMonotonicBuild()
    {
        var dumpsys = "    versionCode=111343 minSdk=24\n    versionName=1.35.7\n";
        Assert.Equal("111343", AdbClient.ParseVersionCode(dumpsys));
    }

    [Fact]
    public void ParseVersionName_MissingReturnsEmpty()
    {
        Assert.Equal("", AdbClient.ParseVersionName("no version here"));
    }

    [Fact]
    public void ParseApkPaths_ExtractsPathAfterColon()
    {
        var pm = "package:/data/app/~~a==/com.auxbrain.egginc-b==/base.apk\n"
               + "package:/data/app/~~a==/com.auxbrain.egginc-b==/split_config.arm64_v8a.apk\n";
        var paths = AdbClient.ParseApkPaths(pm);
        Assert.Equal(2, paths.Count);
        Assert.Contains("/base.apk", paths[0]);
        Assert.EndsWith("split_config.arm64_v8a.apk", paths[1]);
    }

    [Fact]
    public void SelectArmApk_PicksArmSplit()
    {
        var paths = new[]
        {
            "/data/app/x/base.apk",
            "/data/app/x/split_config.arm64_v8a.apk",
            "/data/app/x/split_config.en.apk",
        };
        Assert.EndsWith("arm64_v8a.apk", AdbClient.SelectArmApk(paths));
    }

    [Fact]
    public void SelectArmApk_NoneReturnsEmpty()
    {
        Assert.Equal("", AdbClient.SelectArmApk(new[] { "/data/app/x/base.apk" }));
    }
}

using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoVersionQualityTests {
    [Theory]
    [InlineData("111342", true)]
    [InlineData("72", true)]
    [InlineData("1.35.7.1", false)]
    [InlineData("a1b2c3d4e5f6g7h8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAndroidStyleBuild_DetectsBareIntegers(string? build, bool expected) =>
        Assert.Equal(expected, ProtoVersionQuality.IsAndroidStyleBuild(build));

    [Fact]
    public void Mismatch_IosWithIntegerBuild_IsFlagged() {
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "111342"));
        Assert.Equal("android_build_on_ios", ProtoVersionQuality.BuildQualityFlag("ios", "111342"));
    }

    [Fact]
    public void Mismatch_IosWithDottedOrHashBuild_IsClean() {
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "1.35.7.1"));
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "a1b2c3d4e5f6g7h8"));
        Assert.Null(ProtoVersionQuality.BuildQualityFlag("ios", "1.35.7.1"));
    }

    [Fact]
    public void Mismatch_AndroidWithIntegerBuild_IsClean() {
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("android", "111342"));
        Assert.Null(ProtoVersionQuality.BuildQualityFlag("android", "111342"));
    }

    [Fact]
    public void Mismatch_IsCaseInsensitiveOnPlatform() {
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("iOS", "111342"));
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("IOS", "111342"));
    }

    [Fact]
    public void LatestSortKey_Android_RanksByVersionCode() {
        long older = ProtoVersionQuality.LatestSortKey("android", "111341", "1.35.6");
        long newer = ProtoVersionQuality.LatestSortKey("android", "111342", "1.35.7");
        Assert.True(newer > older);
    }

    [Fact]
    public void LatestSortKey_Ios_RanksByDottedVersion_NotIntegerBuild() {
        long good = ProtoVersionQuality.LatestSortKey("ios", "1.35.7.1", "1.35.7");
        long bad = ProtoVersionQuality.LatestSortKey("ios", "111342", "1.35.7");
        Assert.True(good > bad);
        Assert.Equal(long.MinValue, bad);
    }

    [Fact]
    public void LatestSortKey_Ios_NewerDottedWins() {
        long v1357 = ProtoVersionQuality.LatestSortKey("ios", "1.35.7.1", "1.35.7");
        long v1358 = ProtoVersionQuality.LatestSortKey("ios", "1.35.8.0", "1.35.8");
        Assert.True(v1358 > v1357);
    }

    [Fact]
    public void DottedVersionKey_OrdersComponentsCorrectly() {
        Assert.True(ProtoVersionQuality.DottedVersionKey("1.36.0") > ProtoVersionQuality.DottedVersionKey("1.35.9"));
        Assert.True(ProtoVersionQuality.DottedVersionKey("2.0.0") > ProtoVersionQuality.DottedVersionKey("1.99.99"));
        Assert.Equal(long.MinValue, ProtoVersionQuality.DottedVersionKey(""));
    }
}

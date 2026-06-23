using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

// The computed data-quality rule for registry rows: an iOS row carrying an Android-style integer build
// (the shared wire versionCode leaking into the iOS build key) is flagged, and must not win "latest".
public class ProtoVersionQualityTests
{
    [Theory]
    [InlineData("111342", true)]
    [InlineData("72", true)]
    [InlineData("1.35.7.1", false)]   // dotted iOS build
    [InlineData("a1b2c3d4e5f6g7h8", false)] // hash build (has letters)
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAndroidStyleBuild_DetectsBareIntegers(string? build, bool expected) =>
        Assert.Equal(expected, ProtoVersionQuality.IsAndroidStyleBuild(build));

    [Fact]
    public void Mismatch_IosWithIntegerBuild_IsFlagged()
    {
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "111342"));
        Assert.Equal("android_build_on_ios", ProtoVersionQuality.BuildQualityFlag("ios", "111342"));
    }

    [Fact]
    public void Mismatch_IosWithDottedOrHashBuild_IsClean()
    {
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "1.35.7.1"));
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "a1b2c3d4e5f6g7h8"));
        Assert.Null(ProtoVersionQuality.BuildQualityFlag("ios", "1.35.7.1"));
    }

    [Fact]
    public void Mismatch_AndroidWithIntegerBuild_IsClean()
    {
        // Android builds ARE bare integers - that is correct, never flagged.
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("android", "111342"));
        Assert.Null(ProtoVersionQuality.BuildQualityFlag("android", "111342"));
    }

    [Fact]
    public void Mismatch_IsCaseInsensitiveOnPlatform()
    {
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("iOS", "111342"));
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("IOS", "111342"));
    }

    [Fact]
    public void LatestSortKey_Android_RanksByVersionCode()
    {
        var older = ProtoVersionQuality.LatestSortKey("android", "111341", "1.35.6");
        var newer = ProtoVersionQuality.LatestSortKey("android", "111342", "1.35.7");
        Assert.True(newer > older);
    }

    [Fact]
    public void LatestSortKey_Ios_RanksByDottedVersion_NotIntegerBuild()
    {
        // A real iOS row (dotted build) must outrank a BAD iOS row whose build is an Android integer,
        // even though the integer is numerically large. This is the "thinks it's latest" bug fix.
        var good = ProtoVersionQuality.LatestSortKey("ios", "1.35.7.1", "1.35.7");
        var bad = ProtoVersionQuality.LatestSortKey("ios", "111342", "1.35.7");
        Assert.True(good > bad);
        Assert.Equal(long.MinValue, bad); // bad iOS integer build sorts dead last
    }

    [Fact]
    public void LatestSortKey_Ios_NewerDottedWins()
    {
        var v1357 = ProtoVersionQuality.LatestSortKey("ios", "1.35.7.1", "1.35.7");
        var v1358 = ProtoVersionQuality.LatestSortKey("ios", "1.35.8.0", "1.35.8");
        Assert.True(v1358 > v1357);
    }

    [Fact]
    public void DottedVersionKey_OrdersComponentsCorrectly()
    {
        Assert.True(ProtoVersionQuality.DottedVersionKey("1.36.0") > ProtoVersionQuality.DottedVersionKey("1.35.9"));
        Assert.True(ProtoVersionQuality.DottedVersionKey("2.0.0") > ProtoVersionQuality.DottedVersionKey("1.99.99"));
        Assert.Equal(long.MinValue, ProtoVersionQuality.DottedVersionKey(""));
    }
}

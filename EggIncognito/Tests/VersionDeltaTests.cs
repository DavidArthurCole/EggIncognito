using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class VersionDeltaTests {
    [Fact]
    public void Android_HigherBuild_IsForward() =>
        Assert.Equal(VersionDelta.Forward,
            VersionDeltaCalc.Classify("android", "111358", "1.37.0", "111357", "1.36.4"));

    [Fact]
    public void Android_SameBuild_IsRepeat() =>
        Assert.Equal(VersionDelta.Repeat,
            VersionDeltaCalc.Classify("android", "111357", "1.36.4", "111357", "1.36.4"));

    [Fact]
    public void Android_LowerBuild_IsBackfill() =>
        Assert.Equal(VersionDelta.Backfill,
            VersionDeltaCalc.Classify("android", "111350", "1.36.0", "111357", "1.36.4"));

    [Fact]
    public void NoPrevious_IsForward() =>
        Assert.Equal(VersionDelta.Forward,
            VersionDeltaCalc.Classify("ios", "1.37.0.1", "1.37.0", null, null));

    [Fact]
    public void Ios_WithAndroidStyleBuild_IsUnknown() =>
        Assert.Equal(VersionDelta.Unknown,
            VersionDeltaCalc.Classify("ios", "111340", "1.36.4", "1.37.0.1", "1.37.0"));

    [Fact]
    public void Ios_NewerAppVersion_IsForward() =>
        Assert.Equal(VersionDelta.Forward,
            VersionDeltaCalc.Classify("ios", "1.37.0.1", "1.37.0", "1.36.4.1", "1.36.4"));

    [Fact]
    public void BrokenIosCarve_FlagsClientVersionBuildAndProto() {
        var flaws = ProtoVersionQuality.Flaws("ios", "111340", null, "", false);

        Assert.Contains(ProtoVersionQuality.FlawNoClientVersion, flaws);
        Assert.Contains(ProtoVersionQuality.FlawBuildPlatformMismatch, flaws);
        Assert.Contains(ProtoVersionQuality.FlawNoProto, flaws);
    }

    [Fact]
    public void HealthyCarve_HasNoFlaws() =>
        Assert.Empty(ProtoVersionQuality.Flaws("android", "111358", "72", "abc123", true));
}

using EggIncognito.Services.ProtoExtract;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class ProtoCounterpartsTests {
    private static ProtoRegistryRow Row(
        long id, string platform, string appVersion, string build,
        string? client = null, long? canonical = null, string? sha = null) =>
        new(id, canonical, platform, appVersion, build, client, null, null, sha, null, null, null);

    private static ProtoCounterpart Link(IReadOnlyList<ProtoCounterpart> links, string platform) =>
        links.First(l => string.Equals(l.Platform, platform, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void TargetsSkipTheSourcePlatformAndStayInsideTheKnownSet() {
        IReadOnlyList<string> targets = ProtoCounterparts.Targets("iOS");

        Assert.Contains("android", targets, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ios", targets, StringComparer.OrdinalIgnoreCase);
        Assert.All(targets, t => Assert.Contains(t, ProtoRefParser.Known, StringComparer.OrdinalIgnoreCase));
        Assert.Contains("ios", ProtoCounterparts.Targets("android"), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file")]
    [InlineData("web")]
    public void AnUnknownPlatformHasNoTargets(string? platform) => Assert.Empty(ProtoCounterparts.Targets(platform));

    [Fact]
    public void AnUploadedFileHasNoRegistryRowAndSoNoLinks() {
        var rows = new[] { Row(1, "ios", "1.37.0", "1.37.0.1", "75"), Row(2, "android", "1.37.0", "111358", "75") };

        Assert.Empty(ProtoCounterparts.For(null, rows));
    }

    [Fact]
    public void ARowOnAnUnknownPlatformHasNoLinks() {
        var src = Row(1, "web", "1.37.0", "w37", "75");
        var rows = new[] { src, Row(2, "android", "1.37.0", "111358", "75") };

        Assert.Empty(ProtoCounterparts.For(src, rows));
    }

    [Fact]
    public void AMergedReleaseTranslatesAtTheCanonicalTier() {
        var src = Row(1, "ios", "1.37.0", "1.37.0.1", "75");
        var rows = new[] { src, Row(2, "android", "9.9.9", "111400", "99", canonical: 1) };

        ProtoCounterpart link = Link(ProtoCounterparts.For(src, rows), "android");

        Assert.Equal(VersionLinkKind.Canonical, link.Kind);
        Assert.Equal("111400", link.Row!.Build);
        Assert.True(link.Found);
        Assert.False(link.Weak);
        Assert.Equal("same release", link.Reason);
    }

    [Fact]
    public void ASharedProtoShaTranslatesAtTheShaTier() {
        var src = Row(1, "ios", "1.37.0", "1.37.0.1", "75", sha: "abc");
        var rows = new[] { src, Row(2, "android", "9.9.9", "111400", "99", sha: "abc") };

        ProtoCounterpart link = Link(ProtoCounterparts.For(src, rows), "android");

        Assert.Equal(VersionLinkKind.ProtoSha, link.Kind);
        Assert.Equal("111400", link.Row!.Build);
        Assert.False(link.Weak);
        Assert.Equal("same proto sha", link.Reason);
    }

    [Fact]
    public void IosAgainstAndroidOnOneAppVersionIsNotAGuess() {
        var src = Row(1, "ios", "1.37.0", "1.37.0.1", "75");
        var rows = new[] {
            src,
            Row(2, "ios", "1.36.0", "1.36.0.2", "70"),
            Row(3, "android", "1.37.0", "111358", "70"),
            Row(4, "android", "1.30.0", "111000", "75")
        };

        IReadOnlyList<ProtoCounterpart> links = ProtoCounterparts.For(src, rows);
        ProtoCounterpart link = Link(links, "android");

        Assert.Equal(VersionLinkKind.AppVersion, link.Kind);
        Assert.Equal("111358", link.Row!.Build);
        Assert.Equal("android", link.Row.Platform);
        Assert.False(link.Weak);
        Assert.Equal("same app version", link.Reason);
        Assert.All(links, l => Assert.NotEqual("ios", l.Platform));
    }

    [Fact]
    public void AClientVersionOnlyMatchIsTheWeakestLinkAndReadsAsAGuess() {
        var src = Row(1, "ios", "1.37.0", "1.37.0.1", "75");
        var rows = new[] { src, Row(2, "android", "1.30.0", "111000", "75") };

        ProtoCounterpart link = Link(ProtoCounterparts.For(src, rows), "android");

        Assert.Equal(VersionLinkKind.ClientVersion, link.Kind);
        Assert.Equal("111000", link.Row!.Build);
        Assert.True(link.Found);
        Assert.True(link.Weak);
        Assert.Equal("same client version", link.Reason);
    }

    [Fact]
    public void NoComparableRowYieldsALinkThatFoundNothing() {
        var src = Row(1, "ios", "1.37.0", "1.37.0.1", "75", sha: "abc");
        var rows = new[] { src, Row(2, "android", "1.20.0", "110000", "60", sha: "def") };

        ProtoCounterpart link = Link(ProtoCounterparts.For(src, rows), "android");

        Assert.Equal(VersionLinkKind.None, link.Kind);
        Assert.Null(link.Row);
        Assert.False(link.Found);
        Assert.False(link.Weak);
        Assert.Equal("no match", link.Reason);
    }

    [Fact]
    public void AnEmptyRegistryStillOffersTheTargetPlatformWithNothingBehindIt() {
        var src = Row(1, "ios", "1.37.0", "1.37.0.1", "75");

        ProtoCounterpart link = Link(ProtoCounterparts.For(src, [src]), "android");

        Assert.Equal(VersionLinkKind.None, link.Kind);
        Assert.False(link.Found);
    }
}

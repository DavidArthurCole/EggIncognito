using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoRefTests {
    [Theory]
    [InlineData("ios_1.37.0.1")]
    [InlineData("android_370")]
    [InlineData("ios_1.36.0.2...ios_1.37.0.1")]
    [InlineData("ios_1.36.0.2...ios_1.37.0.1/unified")]
    [InlineData("ios_1.37.0.1/meta")]
    [InlineData("file_4f2a1c9b0d3e")]
    [InlineData("file_4f2a1c9b0d3e...ios_1.37.0.1/split")]
    public void CanonicalFormsRoundTrip(string hash) {
        Assert.Equal(hash, ProtoRefParser.Format(ProtoRefParser.Parse(hash)));
    }

    [Fact]
    public void LeadingHashIsAccepted() {
        var parsed = ProtoRefParser.Parse("#ios_1.37.0.1");
        Assert.Equal("ios", parsed.A!.Platform);
        Assert.Equal("1.37.0.1", parsed.A.Build);
    }

    [Fact]
    public void SessionRefCarriesFileSha() {
        var parsed = ProtoRefParser.Parse("file_4f2a1c9b0d3e");
        Assert.Equal(ProtoRefSource.Session, parsed.A!.Source);
        Assert.Equal("4f2a1c9b0d3e", parsed.A.FileSha);
    }

    [Fact]
    public void BuildKeepsUnderscoresAfterTheFirst() {
        var parsed = ProtoRefParser.Parse("android_370_beta");
        Assert.Equal("android", parsed.A!.Platform);
        Assert.Equal("370_beta", parsed.A.Build);
    }

    [Fact]
    public void DottedBuildsAreNotMistakenForTheSeparator() {
        var parsed = ProtoRefParser.Parse("ios_1.37.0.1");
        Assert.Null(parsed.B);
        Assert.Equal("1.37.0.1", parsed.A!.Build);
    }

    [Fact]
    public void UnknownModeIsDropped() {
        var parsed = ProtoRefParser.Parse("ios_1.37.0.1/wat");
        Assert.Null(parsed.Mode);
        Assert.Equal("1.37.0.1", parsed.A!.Build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("garbage")]
    [InlineData("...")]
    [InlineData("_")]
    public void GarbageYieldsEmptyRef(string? hash) {
        var parsed = ProtoRefParser.Parse(hash);
        Assert.Null(parsed.A);
        Assert.Null(parsed.B);
    }

    [Fact]
    public void ParseNeverThrows() {
        foreach (string h in new[] { "ios_", "_1.37", "a...b...c", "ios_1.37/", "/split" }) {
            ProtoRefParser.Parse(h);
        }
    }

    [Theory]
    [InlineData("notify")]
    [InlineData("notify_7")]
    [InlineData("notify_7_config")]
    [InlineData("#notify_7")]
    public void AnotherComponentsHashNamespaceIsNotClaimed(string hash) {
        var parsed = ProtoRefParser.Parse(hash);
        Assert.Null(parsed.A);
        Assert.Null(parsed.B);
    }

    [Fact]
    public void AnUnknownPlatformIsRejectedRatherThanInvented() {
        Assert.Null(ProtoRefParser.Parse("web_1.37.0.1").A);
    }

    [Fact]
    public void APairIsDroppedWhenOnlyOneSideNamesAKnownPlatform() {
        var parsed = ProtoRefParser.Parse("ios_1.37.0.1...notify_7");
        Assert.Equal("ios", parsed.A!.Platform);
        Assert.Null(parsed.B);
    }

    [Fact]
    public void FormatOfEmptyRefIsEmptyString() {
        Assert.Equal("", ProtoRefParser.Format(new WorkbenchRef(null, null, null)));
    }
}

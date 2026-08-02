using EggIncognito.Data.Services;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class BatchUploadProcessorTests {
    private static DescriptorProtoCarver.ExtractResult Ok() =>
        new(true, "message X {}", "", "sha-x", new[] { "X" }) { AppVersion = "1.0", Build = "b", ClientVersion = 77 };
    private static DescriptorProtoCarver.ExtractResult Fail() =>
        new(false, null, "no proto", null, System.Array.Empty<string>());

    [Fact]
    public void Ok_new_maps_to_staged() {
        var o = BatchUploadProcessor.ProcessBytes([0], "android",
            _ => Ok(), _ => Ok(), StagedProtoStore.OfferResult.Staged);
        Assert.Equal("staged", o.Status);
        Assert.Equal("sha-x", o.ProtoSha);
        Assert.Equal("77", o.ClientVersion);
    }

    [Fact]
    public void Ok_duplicate_maps_to_duplicate() {
        var o = BatchUploadProcessor.ProcessBytes([0], "android",
            _ => Ok(), _ => Ok(), StagedProtoStore.OfferResult.AlreadyInRegistry);
        Assert.Equal("duplicate", o.Status);
    }

    [Fact]
    public void Extract_fail_maps_to_failed() {
        var o = BatchUploadProcessor.ProcessBytes([0], "android",
            _ => Fail(), _ => Fail(), StagedProtoStore.OfferResult.Staged);
        Assert.Equal("failed", o.Status);
        Assert.Equal("no proto", o.Diagnostics);
    }

    [Fact]
    public void Zip_magic_picks_archive_extractor() {
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x00];
        bool usedArchive = false;
        BatchUploadProcessor.ProcessBytes(zip, "android",
            _ => { usedArchive = true; return Ok(); },
            _ => Ok(), StagedProtoStore.OfferResult.Staged);
        Assert.True(usedArchive);
    }
}

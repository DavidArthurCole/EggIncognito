using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

// ApkProtoExtractor pulls the native lib from an APK zip and carves the embedded descriptor. We build a
// synthetic "APK" whose lib/arm64-v8a/libegginc.so is the real carved 1.35.8 descriptor fixture, so the
// zip + lib-selection + carve path is exercised end to end without shipping a real 90MB APK.
public class ApkProtoExtractorTests
{
    [Fact]
    public void Extract_SyntheticApkWithLibegginc_CarvesProto()
    {
        var apk = ZipWith("lib/arm64-v8a/libegginc.so", Fixture());
        var r = ApkProtoExtractor.Extract(apk);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.NotNull(r.Proto);
        Assert.Contains("message EggIncFirstContactRequest {", r.Proto);
        Assert.NotEmpty(r.Messages);
    }

    [Fact]
    public void Extract_ApkWithDescriptorInNonLibEntry_FallsBackToRawScan()
    {
        // No .so entry; the descriptor sits in some other stored entry. The raw-zip-scan fallback finds it.
        var apk = ZipWith("assets/blob.bin", Fixture(), CompressionLevel.NoCompression);
        var r = ApkProtoExtractor.Extract(apk);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Contains("message EggIncFirstContactRequest {", r.Proto);
    }

    [Fact]
    public void Extract_Empty_FailsCleanly()
    {
        var r = ApkProtoExtractor.Extract([]);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Extract_NotAZip_FallsBackToRawScan()
    {
        // A bare Mach-O (the fixture) is not a zip; the fallback scans it directly and still carves.
        var r = ApkProtoExtractor.Extract(Fixture());
        Assert.True(r.Ok, r.Diagnostics);
    }

    private static byte[] ZipWith(string name, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var es = zip.CreateEntry(name, level).Open())
            es.Write(content);
        return ms.ToArray();
    }

    private static byte[] Fixture()
    {
        var dir = Path.GetDirectoryName(SourcePath())!;
        return File.ReadAllBytes(Path.Combine(dir, "egginc-1.35.8-descriptors.bin"));
    }

    static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}

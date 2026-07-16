using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;


public class ArchiveProtoExtractorTests
{
    [Fact]
    public void Extract_Apk_LibEgginc_Carves()
    {
        var apk = ZipWith("lib/arm64-v8a/libegginc.so", Fixture());
        var r = ArchiveProtoExtractor.Extract(apk);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Contains("message EggIncFirstContactRequest {", r.Proto);
        Assert.NotEmpty(r.Messages);
    }

    [Fact]
    public void Extract_Ipa_CompressedAppExecutable_Carves()
    {
       
       
        var ipa = ZipWith("Payload/EggInc.app/egginc", Fixture(), CompressionLevel.Optimal);
        var r = ArchiveProtoExtractor.Extract(ipa);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Contains("message EggIncFirstContactRequest {", r.Proto);
    }

    [Fact]
    public void Extract_Ipa_IgnoresAppResources_FindsExecutable()
    {
       
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "Payload/EggInc.app/Info.plist", [1, 2, 3]);
            Write(zip, "Payload/EggInc.app/egginc", Fixture());
            Write(zip, "Payload/EggInc.app/Assets.car", [4, 5, 6]);
        }
        var r = ArchiveProtoExtractor.Extract(ms.ToArray());
        Assert.True(r.Ok, r.Diagnostics);
    }

    [Fact]
    public void Extract_DescriptorInStoredEntry_FallsBackToRawScan()
    {
        var apk = ZipWith("assets/blob.bin", Fixture(), CompressionLevel.NoCompression);
        var r = ArchiveProtoExtractor.Extract(apk);
        Assert.True(r.Ok, r.Diagnostics);
    }

    [Fact]
    public void Extract_Ipa_ReadsVersionFromInfoPlist()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "Payload/EggInc.app/egginc", Fixture());
            Write(zip, "Payload/EggInc.app/Info.plist", System.Text.Encoding.UTF8.GetBytes(
                "<plist><dict><key>CFBundleShortVersionString</key><string>1.35.6</string>"
                + "<key>CFBundleVersion</key><string>1.35.6.3</string></dict></plist>"));
        }
        var r = ArchiveProtoExtractor.Extract(ms.ToArray());
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal("1.35.6", r.AppVersion);
       
       
        Assert.Null(r.Build);
    }

    [Fact]
    public void Extract_Empty_FailsCleanly() => Assert.False(ArchiveProtoExtractor.Extract([]).Ok);

    [Fact]
    public void Extract_NotAZip_RawScanCarves()
    {
       
        var r = ArchiveProtoExtractor.Extract(Fixture());
        Assert.True(r.Ok, r.Diagnostics);
    }

    [Fact]
    public void ApkProtoExtractor_Alias_StillWorks()
    {
        var r = ApkProtoExtractor.Extract(ZipWith("lib/arm64-v8a/libegginc.so", Fixture()));
        Assert.True(r.Ok, r.Diagnostics);
    }

    private static byte[] ZipWith(string name, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            Write(zip, name, content, level);
        return ms.ToArray();
    }

    private static void Write(ZipArchive zip, string name, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var es = zip.CreateEntry(name, level).Open();
        es.Write(content);
    }

    private static byte[] Fixture()
    {
        var dir = Path.GetDirectoryName(SourcePath())!;
        return File.ReadAllBytes(Path.Combine(dir, "egginc-1.35.8-descriptors.bin"));
    }

    static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}

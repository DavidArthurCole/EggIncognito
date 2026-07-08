using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

// Fixture arm_split_AndroidManifest.bin is the binary AXML from Egg Inc 1.35.7's config.arm64_v8a.apk, versionCode 111344.
public class ApkVersionCodeTests
{
    private const string ExpectedVersionCode = "111344";

    [Fact]
    public void Read_NullBytes_ReturnsNull() => Assert.Null(ApkVersionCode.Read(null!));

    [Fact]
    public void Read_EmptyBytes_ReturnsNull() => Assert.Null(ApkVersionCode.Read([]));

    [Fact]
    public void Read_GarbageBytes_ReturnsNull() =>
        Assert.Null(ApkVersionCode.Read([0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33]));

    [Fact]
    public void Read_ZipWithoutManifest_ReturnsNull() =>
        Assert.Null(ApkVersionCode.Read(ZipWithEntry("classes.dex", [1, 2, 3])));

    [Fact]
    public void Read_RealArmSplitManifestZip_ReturnsVersionCode() =>
        Assert.Equal(ExpectedVersionCode, ApkVersionCode.Read(ZipWithEntry("AndroidManifest.xml", Fixture())));

    [Fact]
    public void ParseAxml_RealArmSplitManifest_ReturnsVersionCode() =>
        Assert.Equal(ExpectedVersionCode, ApkVersionCode.ParseAxml(Fixture()));

    private static byte[] Fixture()
    {
        var dir = Path.GetDirectoryName(SourcePath())!;
        return File.ReadAllBytes(Path.Combine(dir, "arm_split_AndroidManifest.bin"));
    }

    private static byte[] ZipWithEntry(string name, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var es = zip.CreateEntry(name).Open())
            es.Write(content);
        return ms.ToArray();
    }

    static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}

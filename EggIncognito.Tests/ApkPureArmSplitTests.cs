using System.IO.Compression;
using System.Text;
using EggIncognito.Services.Backfill.Sources;

namespace EggIncognito.Tests;

public class ApkPureArmSplitTests
{
    private static readonly byte[] ArmSplitBody = Encoding.ASCII.GetBytes("ARM64-SPLIT-PAYLOAD");
    private static readonly byte[] BaseBody = Encoding.ASCII.GetBytes("BASE-APK-PAYLOAD");

    private static byte[] BuildXapk(params (string name, byte[] body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var e = zip.CreateEntry(name);
                using var s = e.Open();
                s.Write(body, 0, body.Length);
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void ApkPure_Named_Split_Is_Extracted()
    {
        var xapk = BuildXapk(
            ("com.auxbrain.egginc.apk", BaseBody),
            ("config.arm64_v8a.apk", ArmSplitBody),
            ("manifest.json", Encoding.ASCII.GetBytes("""{"version_code":"111344"}""")));

        var got = ApkPureSource.ExtractArmSplit(xapk);
        Assert.NotNull(got);
        Assert.Equal(ArmSplitBody, got);
    }

    [Fact]
    public void Device_Spelling_Split_Is_Extracted()
    {
        var xapk = BuildXapk(
            ("com.auxbrain.egginc.apk", BaseBody),
            ("split_config.arm64_v8a.apk", ArmSplitBody));

        var got = ApkPureSource.ExtractArmSplit(xapk);
        Assert.NotNull(got);
        Assert.Equal(ArmSplitBody, got);
    }

    [Fact]
    public void Garbage_Blob_Is_Null()
    {
        Assert.Null(ApkPureSource.ExtractArmSplit(Encoding.ASCII.GetBytes("this is not a zip archive")));
        Assert.Null(ApkPureSource.ExtractArmSplit([]));
    }

    [Fact]
    public void Base_Only_Bundle_Is_Null()
    {
        var xapk = BuildXapk(
            ("com.auxbrain.egginc.apk", BaseBody),
            ("config.en.apk", Encoding.ASCII.GetBytes("LOCALE")),
            ("config.xxhdpi.apk", Encoding.ASCII.GetBytes("DENSITY")));

        Assert.Null(ApkPureSource.ExtractArmSplit(xapk));
    }
}

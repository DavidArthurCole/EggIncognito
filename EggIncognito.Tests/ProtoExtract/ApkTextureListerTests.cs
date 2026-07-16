using System.IO.Compression;
using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public sealed class ApkTextureListerTests
{
    private static byte[] Apk(params (string Path, string Body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, body) in entries)
            {
                var e = zip.CreateEntry(path);
                using var s = e.Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                s.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void Lists_only_png_stems_under_the_texture_dir()
    {
        var apk = Apk(
            ("assets/textures-etc1png-med/b_icon_quantum_bulb.png", "A"),
            ("assets/textures-etc1png-med/afx_ornate_gusset_1.png", "B"),
            ("assets/rpos/coop.rpo", "C"),
            ("assets/textures-etc1png-med/notes.txt", "D"));

        var stems = ApkTextureLister.ListStems(apk);

        Assert.Contains("b_icon_quantum_bulb", stems);
        Assert.Contains("afx_ornate_gusset_1", stems);
        Assert.DoesNotContain("coop", stems);
        Assert.DoesNotContain("notes", stems);
    }

    [Fact]
    public void Reads_a_named_texture_by_stem()
    {
        var apk = Apk(("assets/textures-etc1png-med/b_icon_dilithium_bulb.png", "PNGDATA"));

        var bytes = ApkTextureLister.ReadStem(apk, "b_icon_dilithium_bulb");

        Assert.NotNull(bytes);
        Assert.Equal("PNGDATA", Encoding.UTF8.GetString(bytes!));
    }

    [Fact]
    public void Unknown_stem_returns_null()
    {
        var apk = Apk(("assets/textures-etc1png-med/b_icon_dilithium_bulb.png", "X"));
        Assert.Null(ApkTextureLister.ReadStem(apk, "b_icon_missing"));
    }
}

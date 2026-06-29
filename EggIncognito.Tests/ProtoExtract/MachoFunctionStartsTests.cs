using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class MachoFunctionStartsTests
{
    [Fact]
    public void Read_ReturnsEmpty_WhenNoTable()
    {
        // SyntheticMacho emits no LC_FUNCTION_STARTS, so the reader returns empty (not a throw).
        var bin = SyntheticMacho.Build(new byte[64], [new SyntheticMacho.Sym("__ZN1A1fEv", SyntheticMacho.TextVm)]);
        Assert.Empty(MachoFunctionStarts.Read(bin));
    }

    [Fact]
    public void Read_ReturnsEmpty_OnGarbage() => Assert.Empty(MachoFunctionStarts.Read(new byte[100]));

    [Fact]
    public void Read_RealStrippedBinary_HasManySortedStarts()
    {
        var bin = StrippedExec();
        if (bin is null) return;
        var starts = MachoFunctionStarts.Read(bin);
        // the stripped App Store build keeps LC_FUNCTION_STARTS even with no symbol names.
        Assert.True(starts.Count > 50_000, $"starts={starts.Count}");
        for (int i = 1; i < starts.Count; i++) Assert.True(starts[i] > starts[i - 1], "starts must be ascending");
        Assert.All(starts, s => Assert.InRange(s, 0, bin.Length - 1));
    }

    private static byte[]? StrippedExec()
    {
        string? dir = null;
        foreach (var rel in new[] { "../../../../captures/ipas", "../../../../../captures/ipas" })
        {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (Directory.Exists(full)) { dir = full; break; }
        }
        if (dir is null) return null;
        var path = Path.Combine(dir, "Egg-Inc-IPAOMTK.COM_latest.ipa");
        if (!File.Exists(path)) return null;
        using var zip = ZipFile.OpenRead(path);
        var e = zip.Entries.FirstOrDefault(en =>
        {
            var f = en.FullName;
            if (!f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)) return false;
            var i = f.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            var rest = f[(i + 5)..];
            return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.');
        });
        if (e is null) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

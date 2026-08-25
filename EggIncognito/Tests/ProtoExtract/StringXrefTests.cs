using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class StringXrefTests {
    private static bool TryLoadAndroid(out byte[] bin) {
        bin = [];
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx"))) dir = dir.Parent;
        if (dir is null) return false;

        string path = Path.Combine(dir.FullName, "EggIncognito", "captures", "egginc-android-1.37.so");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 1_000_000) return false;

        bin = File.ReadAllBytes(path);
        return true;
    }

    [Fact]
    public void MachO_GetPeriodicals_XrefLandsOnHttpHelper() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var hits = StringLocator.Find(bin, "ei/get_periodicals");
        Assert.NotEmpty(hits);

        int totalSites = 0;
        bool httpHelper = false;
        foreach (var h in hits) {
            var scan = Arm64StringXrefScanner.Scan(bin, h.Va);
            totalSites += scan.Total;
            if (scan.Sites.Any(s => s.Symbol.Contains("HttpHelper", StringComparison.Ordinal))) httpHelper = true;
        }

        Assert.True(totalSites >= 1, "no xref site for get_periodicals path");
        Assert.True(httpHelper, "no HttpHelper-attributed xref site for get_periodicals path");
    }

    [Fact]
    public void Android_ZoomZoom_IsLocatableAndReferenced() {
        if (!TryLoadAndroid(out var bin)) return;

        var hits = StringLocator.Find(bin, "zoom_zoom");
        Assert.NotEmpty(hits);

        int totalSites = 0;
        foreach (var h in hits) {
            var scan = Arm64StringXrefScanner.Scan(bin, h.Va);
            totalSites += scan.Total;
        }

        Assert.True(totalSites >= 1, "no xref site for zoom_zoom on ELF");
    }

    [Fact]
    public void Android_ZoomZoomMission_IsDeadCode() {
        if (!TryLoadAndroid(out var bin)) return;

        var syms = ElfSymbols.Read(bin);
        if (!MachoSymbols.TryFindFunc(syms, ["zoomZoomMission"], out var fr)) return;

        var scan = Arm64CallXrefScanner.Scan(bin, fr.Start);
        Assert.False(scan.Reachable, "zoomZoomMission has no references and should read as dead code");
        Assert.Empty(scan.Sites);
    }

    [Fact]
    public void Android_CalledHelper_HasBlCallers() {
        if (!TryLoadAndroid(out var bin)) return;

        var scan = Arm64CallXrefScanner.Scan(bin, 0x161cbc0);
        Assert.True(scan.Total >= 9, $"expected at least 9 call sites, got {scan.Total}");
        Assert.Contains(scan.Sites, s => s.Via == "bl");
    }
}

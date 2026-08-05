using EggIncognito.Services;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EndpointCatalogRebuilderTests {
    private static string? FindContentRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx"))) dir = dir.Parent;
        return dir is null ? null : Path.Combine(dir.FullName, "EggIncognito");
    }

    private static bool TryLoadAndroid(out byte[] bin, out string contentRoot) {
        bin = [];
        contentRoot = FindContentRoot() ?? "";
        if (contentRoot.Length == 0) return false;

        string path = Path.Combine(contentRoot, "captures", "egginc-android-1.37.so");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 1_000_000) return false;

        bin = File.ReadAllBytes(path);
        return true;
    }

    [Fact]
    public void ExtractAndFilter_DiscoversI18nRoutes_SkipsExcludedPaths() {
        if (!TryLoadAndroid(out var bin, out var contentRoot)) return;

        var syms = ElfSymbols.Read(bin);
        var extracted = EndpointCatalogExtractor.ExtractWith(bin, syms);
        Assert.True(extracted.Ok, extracted.Diagnostics);

        var yaml = RouteCatalog.ForRepo(contentRoot);
        var kept = EndpointCatalogRebuilder.Filter(extracted.Endpoints, yaml.ExcludedPaths);

        Assert.Contains(kept, e => e.Path == "ei_i18n/get_translation_pack");
        Assert.Contains(kept, e => e.Path == "ei_i18n/get_translations");
        Assert.DoesNotContain(kept, e => e.Path == "ei/kb");
        Assert.DoesNotContain(kept, e => e.Path == "ei_afx/zoom_zoom");
    }

    [Fact]
    public void ExcludedPaths_ContainsOnlyKnownFalseFlags() {
        string? contentRoot = FindContentRoot();
        if (contentRoot is null) return;

        var yaml = RouteCatalog.ForRepo(contentRoot);
        Assert.Contains("ei/kb", yaml.ExcludedPaths);
        Assert.Contains("ei_afx/zoom_zoom", yaml.ExcludedPaths);
        Assert.Equal(2, yaml.ExcludedPaths.Count);
        Assert.DoesNotContain("ei/has_voted", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei/shell_showcase_view_poll", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei/confirm_gift_delivery", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei_srv/sub_change_hint", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei_ctx/mark_evaluation_read", yaml.ExcludedPaths);
    }

    [Fact]
    public void Filter_DropsOnlyFalseFlags_KeepsNotYetMockedPaths() {
        string? contentRoot = FindContentRoot();
        if (contentRoot is null) return;

        var yaml = RouteCatalog.ForRepo(contentRoot);
        var descriptors = new[] {
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/kb", null, null, false, false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei_afx/zoom_zoom", null, null, false, false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/has_voted", null, null, false, false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/shell_showcase_view_poll", null, null, false,
                false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/confirm_gift_delivery", null, null, false,
                false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei_srv/sub_change_hint", null, null, false,
                false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei_ctx/mark_evaluation_read", null, null, false,
                false)
        };

        var kept = EndpointCatalogRebuilder.Filter(descriptors, yaml.ExcludedPaths);

        Assert.DoesNotContain(kept, e => e.Path == "ei/kb");
        Assert.DoesNotContain(kept, e => e.Path == "ei_afx/zoom_zoom");
        Assert.Contains(kept, e => e.Path == "ei/has_voted");
        Assert.Contains(kept, e => e.Path == "ei/shell_showcase_view_poll");
        Assert.Contains(kept, e => e.Path == "ei/confirm_gift_delivery");
        Assert.Contains(kept, e => e.Path == "ei_srv/sub_change_hint");
        Assert.Contains(kept, e => e.Path == "ei_ctx/mark_evaluation_read");
    }
}

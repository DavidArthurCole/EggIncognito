using Ei;
using EggIncognito.Services.ProtoExtract;
using Google.Protobuf;

namespace EggIncognito.Tests.ProtoExtract;

// ShellCatalog indexes the shells in a DLCCatalog. A synthetic catalog proves the parse + url + asset-type
// grouping; the real captured config (captures/config.json), when present, proves it against the live shape
// (4138 shells, every one with a resolvable .rpoz url).
public class ShellCatalogTests
{
    [Fact]
    public void FromCatalog_ResolvesPrimaryPieceUrl()
    {
        var cat = new DLCCatalog();
        var shell = new ShellSpec { Identifier = "ei_depot_1_black_white", Name = "Black & White", ModifiedGeometry = true };
        shell.PrimaryPiece = new ShellSpec.Types.ShellPiece
        {
            AssetType = ShellSpec.Types.AssetType.Depot1,
            Dlc = new DLCItem { Name = "ei_depot_1_black_white", Directory = "shells", Ext = "rpo", Checksum = "abc",
                Url = "https://www.auxbrain.com/dlc/shells/ei_depot_1_black_white_hash.rpoz" },
        };
        cat.Shells.Add(shell);

        var shells = ShellCatalog.FromCatalog(cat);
        var s = Assert.Single(shells);
        Assert.Equal("ei_depot_1_black_white", s.Identifier);
        Assert.Equal("Depot1", s.AssetType);
        Assert.Equal("https://www.auxbrain.com/dlc/shells/ei_depot_1_black_white_hash.rpoz", s.Url);
        Assert.True(s.ModifiedGeometry);
    }

    [Fact]
    public void ForAssetType_FiltersByType()
    {
        var cat = new DLCCatalog();
        cat.Shells.Add(Shell("a", ShellSpec.Types.AssetType.Chicken));
        cat.Shells.Add(Shell("b", ShellSpec.Types.AssetType.Depot1));
        Assert.Single(ShellCatalog.ForAssetType(cat, "CHICKEN"));
        Assert.Single(ShellCatalog.ForAssetType(cat, "chicken")); // case-insensitive
        Assert.Empty(ShellCatalog.ForAssetType(cat, "HABITAT"));
    }

    [Fact]
    public void ById_FindsShell()
    {
        var cat = new DLCCatalog();
        cat.Shells.Add(Shell("ei_silo_x", ShellSpec.Types.AssetType.Silo0Small));
        Assert.NotNull(ShellCatalog.ById(cat, "ei_silo_x"));
        Assert.Null(ShellCatalog.ById(cat, "missing"));
    }

    [Fact]
    public void FromCatalog_RealConfig_HasManyShells()
    {
        var json = ConfigJson();
        if (json is null) return; // fixture absent (CI): synthetic tests cover the logic

        var cfg = ConfigResponse.Parser.ParseJson(json);
        Assert.NotNull(cfg.DlcCatalog);
        var shells = ShellCatalog.FromCatalog(cfg.DlcCatalog);
        Assert.True(shells.Count > 1000, $"expected thousands of shells, got {shells.Count}");
        // every shell resolves to an auxbrain CDN url.
        Assert.All(shells, s => Assert.Contains("auxbrain.com/dlc", s.Url));
        // chickens are a known asset type with shells.
        Assert.NotEmpty(ShellCatalog.ForAssetType(cfg.DlcCatalog, "CHICKEN"));
    }

    private static ShellSpec Shell(string id, ShellSpec.Types.AssetType type)
    {
        var s = new ShellSpec { Identifier = id };
        s.PrimaryPiece = new ShellSpec.Types.ShellPiece
        {
            AssetType = type,
            Dlc = new DLCItem { Url = $"https://www.auxbrain.com/dlc/shells/{id}.rpoz" },
        };
        return s;
    }

    private static string? ConfigJson()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "captures", "config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "captures", "config.json"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        return null;
    }
}

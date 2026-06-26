using Ei;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

// ShipShellResolver turns a DLCCatalog into per-ship CDN urls for the 4 orbital ships not bundled in the
// app. These build synthetic catalogs (the real one comes from a live/captured ei/get_config) to prove the
// url composition, the url-wins-over-directory rule, the shell-object LOD walk, and lowest-LOD preference.
public class ShipShellResolverTests
{
    private static readonly string[] Afx =
        ["afx_ship_galeggtica", "afx_ship_defihent", "afx_ship_voyegger", "afx_ship_henerprise"];

    [Fact]
    public void Resolve_BuildsUrlFromDirectoryAndName()
    {
        var cat = new DLCCatalog();
        cat.Items.Add(new DLCItem { Name = "afx_ship_galeggtica", Directory = "shells", Ext = "rpoz", Compressed = true, Checksum = "abc123" });

        var r = ShipShellResolver.Resolve(cat, Afx);
        var ship = Assert.Single(r);
        Assert.Equal("afx_ship_galeggtica", ship.AfxName);
        Assert.Equal("https://www.auxbrain.com/dlc/shells/afx_ship_galeggtica.rpoz", ship.Url);
        Assert.True(ship.Compressed);
        Assert.Equal("abc123", ship.Checksum);
    }

    [Fact]
    public void Resolve_AbsoluteUrlWins()
    {
        var cat = new DLCCatalog();
        cat.Items.Add(new DLCItem
        {
            Name = "afx_ship_voyegger", Directory = "shells", Ext = "rpoz",
            Url = "https://www.auxbrain.com/dlc/special/voyegger_v2.rpoz",
        });
        var ship = Assert.Single(ShipShellResolver.Resolve(cat, Afx));
        Assert.Equal("https://www.auxbrain.com/dlc/special/voyegger_v2.rpoz", ship.Url);
    }

    [Fact]
    public void Resolve_WalksShellObjectLodPieces_PrefersLowestLod()
    {
        var cat = new DLCCatalog();
        var obj = new ShellObjectSpec { Identifier = "henerprise", Name = "Henerprise" };
        obj.Pieces.Add(new ShellObjectSpec.Types.LODPiece
        {
            Lod = 2,
            Dlc = new DLCItem { Name = "afx_ship_henerprise_lod2", Directory = "shells", Ext = "rpoz" },
        });
        obj.Pieces.Add(new ShellObjectSpec.Types.LODPiece
        {
            Lod = 0,
            Dlc = new DLCItem { Name = "afx_ship_henerprise", Directory = "shells", Ext = "rpoz" },
        });
        cat.ShellObjects.Add(obj);

        var ship = Assert.Single(ShipShellResolver.Resolve(cat, Afx));
        Assert.Equal(0, ship.Lod);
        Assert.Equal("afx_ship_henerprise", ship.MatchedItemName); // lowest LOD = highest detail
    }

    [Fact]
    public void Resolve_WalksShellSpecPieces()
    {
        var cat = new DLCCatalog();
        var shell = new ShellSpec { Identifier = "defihent" };
        shell.Pieces.Add(new ShellSpec.Types.ShellPiece
        {
            Dlc = new DLCItem { Name = "afx_ship_defihent", Directory = "shells", Ext = "rpoz" },
        });
        cat.Shells.Add(shell);

        var ship = Assert.Single(ShipShellResolver.Resolve(cat, Afx));
        Assert.Equal("afx_ship_defihent", ship.AfxName);
    }

    [Fact]
    public void Resolve_PrefixMatch_HandlesLodSuffix()
    {
        // when only a suffixed item exists, the prefix match still ties it to the ship.
        var cat = new DLCCatalog();
        cat.Items.Add(new DLCItem { Name = "afx_ship_galeggtica_high", Directory = "shells", Ext = "rpoz" });
        var ship = Assert.Single(ShipShellResolver.Resolve(cat, Afx));
        Assert.Equal("afx_ship_galeggtica", ship.AfxName);
        Assert.Equal("afx_ship_galeggtica_high", ship.MatchedItemName);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsEmpty()
    {
        var cat = new DLCCatalog();
        cat.Items.Add(new DLCItem { Name = "ei_silo_3_large", Directory = "shells", Ext = "rpoz" });
        Assert.Empty(ShipShellResolver.Resolve(cat, Afx));
    }
}

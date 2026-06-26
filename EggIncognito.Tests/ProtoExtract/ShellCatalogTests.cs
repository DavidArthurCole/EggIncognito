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
    public void ConfigJson_RoundTrip_PreservesDlcCatalog()
    {
        // The ingest-json + StoreAsync path: ParseJson(decoded json) -> JsonFormatter.Format -> ParseJson.
        // Proves the catalog survives the round-trip (not the 0-shells regression where a husk was stored).
        var json = ConfigJson();
        if (json is null) return;

        var cfg = ConfigResponse.Parser.ParseJson(json);
        var shells = cfg.DlcCatalog?.Shells.Count ?? 0;
        Assert.True(shells > 1000, $"parse lost shells: {shells}");

        var reformatted = Google.Protobuf.JsonFormatter.Default.Format(cfg); // what StoreAsync writes
        var reparsed = ConfigResponse.Parser.ParseJson(reformatted); // what a later read does
        Assert.Equal(shells, reparsed.DlcCatalog?.Shells.Count ?? 0);
    }

    [Fact]
    public void InnerConfigProto_DirectParse_KeepsShells_WrappedAsAuthMsgIsHusk()
    {
        // Models the two ingest inputs. (1) The inflate-step base64 = the inner ConfigResponse proto: a
        // DIRECT ParseFrom must keep the shells. (2) The same bytes wrapped in an AuthenticatedMessage:
        // a direct ParseFrom-as-ConfigResponse yields a husk (lenient proto), so the ingest must prefer the
        // unwrapped parse. This is the 0-shells bug the best-parse ingest fixes.
        var json = ConfigJson();
        if (json is null) return;
        var full = ConfigResponse.Parser.ParseJson(json);
        var fullShells = full.DlcCatalog?.Shells.Count ?? 0;
        Assert.True(fullShells > 1000);

        var innerBytes = full.ToByteArray();
        // (1) direct parse of the inner proto: shells survive.
        var direct = ConfigResponse.Parser.ParseFrom(innerBytes);
        Assert.Equal(fullShells, direct.DlcCatalog?.Shells.Count ?? 0);

        // (2) wrap it; a naive ParseFrom-as-ConfigResponse of the WRAPPED bytes loses the catalog.
        var wrapped = new Ei.AuthenticatedMessage { Message = Google.Protobuf.ByteString.CopyFrom(innerBytes) }.ToByteArray();
        ConfigResponse husk;
        try { husk = ConfigResponse.Parser.ParseFrom(wrapped); }
        catch { husk = new ConfigResponse(); }
        Assert.True((husk.DlcCatalog?.Shells.Count ?? 0) < fullShells, "wrapped bytes should not parse to the full catalog directly");
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

    [Fact]
    public void Objects_ResolvesChickenWithAnchorAndNoHatsFlag()
    {
        var cat = new DLCCatalog();
        var chicken = new ShellObjectSpec { Identifier = "ei_chicken_base", Name = "Base", AssetType = ShellSpec.Types.AssetType.Chicken, NoHats = false };
        chicken.Metadata.Add(new[] { 0.0, 0.5, -0.1, 1.2 });
        chicken.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 1, Dlc = new DLCItem { Url = "https://www.auxbrain.com/dlc/shellobjects/chicken_lod1.rpoz" } });
        chicken.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 0, Dlc = new DLCItem { Url = "https://www.auxbrain.com/dlc/shellobjects/chicken_lod0.rpoz" } });
        cat.ShellObjects.Add(chicken);

        var objs = ShellCatalog.Objects(cat);
        var o = Assert.Single(objs);
        Assert.Equal("ei_chicken_base", o.Identifier);
        Assert.Equal("Chicken", o.AssetType);
        Assert.Equal(new[] { 0.0, 0.5, -0.1, 1.2 }, o.Anchor);
        Assert.False(o.NoHats);
        Assert.Equal("https://www.auxbrain.com/dlc/shellobjects/chicken_lod0.rpoz", o.Url);
    }

    [Fact]
    public void Chickens_And_Hats_FilterByAssetType()
    {
        var cat = new DLCCatalog();
        cat.ShellObjects.Add(Obj("c1", ShellSpec.Types.AssetType.Chicken));
        cat.ShellObjects.Add(Obj("h1", ShellSpec.Types.AssetType.Hat));
        cat.ShellObjects.Add(Obj("h2", ShellSpec.Types.AssetType.Hat));
        Assert.Single(ShellCatalog.Chickens(cat));
        Assert.Equal(2, ShellCatalog.Hats(cat).Count);
    }

    [Fact]
    public void ObjectById_FindsObject()
    {
        var cat = new DLCCatalog();
        cat.ShellObjects.Add(Obj("ei_hat_x", ShellSpec.Types.AssetType.Hat));
        Assert.NotNull(ShellCatalog.ObjectById(cat, "ei_hat_x"));
        Assert.Null(ShellCatalog.ObjectById(cat, "missing"));
    }

    [Fact]
    public void NoHatsChicken_HasEmptyAnchor()
    {
        var cat = new DLCCatalog();
        var polish = new ShellObjectSpec { Identifier = "ei_chicken_polish", AssetType = ShellSpec.Types.AssetType.Chicken, NoHats = true };
        polish.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 0, Dlc = new DLCItem { Url = "https://www.auxbrain.com/dlc/shellobjects/polish.rpoz" } });
        cat.ShellObjects.Add(polish);
        var o = Assert.Single(ShellCatalog.Chickens(cat));
        Assert.True(o.NoHats);
        Assert.Empty(o.Anchor);
    }

    [Fact]
    public void HatWearingChicken_WithoutMetadata_GetsDefaultAnchor()
    {
        var cat = new DLCCatalog();
        // a chicken that wears a hat (noHats=false) but ships no metadata override.
        cat.ShellObjects.Add(Obj("ei_chicken_skis", ShellSpec.Types.AssetType.Chicken));
        var o = Assert.Single(ShellCatalog.Chickens(cat));
        Assert.False(o.NoHats);
        Assert.Equal(ShellCatalog.DefaultChickenAnchor, o.Anchor);
    }

    [Fact]
    public void RealConfig_HasChickensWithAnchors_AndHats()
    {
        var json = ConfigJson();
        if (json is null) return; // fixture absent (CI)
        var cfg = ConfigResponse.Parser.ParseJson(json);
        var chickens = ShellCatalog.Chickens(cfg.DlcCatalog!);
        var hats = ShellCatalog.Hats(cfg.DlcCatalog!);
        Assert.True(chickens.Count > 50, $"expected many chickens, got {chickens.Count}");
        Assert.True(hats.Count > 50, $"expected many hats, got {hats.Count}");
        Assert.Contains(chickens, c => !c.NoHats && c.Anchor.Count == 4);
        Assert.All(chickens, c => Assert.Contains("auxbrain.com/dlc", c.Url));
    }

    private static ShellObjectSpec Obj(string id, ShellSpec.Types.AssetType type)
    {
        var o = new ShellObjectSpec { Identifier = id, AssetType = type };
        o.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 0, Dlc = new DLCItem { Url = $"https://www.auxbrain.com/dlc/shellobjects/{id}.rpoz" } });
        return o;
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

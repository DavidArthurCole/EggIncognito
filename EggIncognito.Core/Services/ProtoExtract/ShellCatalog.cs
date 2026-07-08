using Ei;

namespace EggIncognito.Services.ProtoExtract;

// Indexes the shells in a DLCCatalog (the ei/get_config config). A shell is a cosmetic mesh that replaces a
// model's look for a given asset type (a chicken skin, a depot reskin, a hab variant). Each carries a
// DLCItem with the CDN url of its .rpoz mesh, so a shell renders through the same download + RpoMeshDecoder
// path as any other mesh. This is the read model behind the shell viewer + the local shell DB.
public static class ShellCatalog
{
    private const string CdnBase = "https://www.auxbrain.com/dlc";

    // One shell: its identifier, display set, the asset type it applies to (DEPOT_1, CHICKEN, ...), and the
    // resolved mesh url + checksum. AssetType groups shells by the model they fit, so the viewer can offer
    // only the shells valid for the loaded model.
    public sealed record Shell(string Identifier, string? Name, string AssetType, string Url, string? Checksum, bool ModifiedGeometry, string? SetIdentifier = null);

    // A chicken, hat, or other interactive shell object. Carries the hat anchor (metadata = [x, hatY, hatZ,
    // scale] on chickens) and the noHats flag.
    public sealed record ShellObject(
        string Identifier, string? Name, string AssetType, string Url, string? Checksum,
        IReadOnlyList<double> Anchor, bool NoHats);

    // Every shell in the catalog with a resolvable mesh url. Pulls the primary piece (the main mesh); shells
    // without a primary piece dlc are skipped. ShellObjects (LOD pieces) are included via their first piece.
    public static IReadOnlyList<Shell> FromCatalog(DLCCatalog catalog)
    {
        var shells = new List<Shell>();
        if (catalog is null) return shells;

        foreach (var s in catalog.Shells)
        {
            var piece = s.PrimaryPiece ?? s.Pieces.FirstOrDefault();
            if (piece?.Dlc is not { } dlc) continue;
            var url = Url(dlc);
            if (url is null) continue;
            shells.Add(new Shell(s.Identifier ?? "", NullIfEmpty(s.Name), AssetTypeName(piece.AssetType),
                url, NullIfEmpty(dlc.Checksum), s.ModifiedGeometry, NullIfEmpty(s.SetIdentifier)));
        }

        foreach (var o in catalog.ShellObjects)
        {
            // shell objects nest LOD pieces; take the lowest LOD (highest detail) with a dlc.
            var piece = o.Pieces.Where(p => p.Dlc is not null).OrderBy(p => p.Lod).FirstOrDefault();
            if (piece?.Dlc is not { } dlc) continue;
            var url = Url(dlc);
            if (url is null) continue;
            shells.Add(new Shell(o.Identifier ?? "", NullIfEmpty(o.Name), AssetTypeName(o.AssetType),
                url, NullIfEmpty(dlc.Checksum), false));
        }

        return shells;
    }

    // The shells that fit a given asset type, e.g. "CHICKEN". Case-insensitive.
    public static IReadOnlyList<Shell> ForAssetType(DLCCatalog catalog, string assetType) =>
        FromCatalog(catalog).Where(s => string.Equals(s.AssetType, assetType, StringComparison.OrdinalIgnoreCase)).ToList();

    // One shell by identifier (case-sensitive identifiers in the catalog), or null.
    public static Shell? ById(DLCCatalog catalog, string identifier) =>
        FromCatalog(catalog).FirstOrDefault(s => string.Equals(s.Identifier, identifier, StringComparison.Ordinal));

    // The standard chicken head anchor [x, hatY, hatZ, scale], used when a hat-wearing chicken has no explicit metadata.
    public static readonly IReadOnlyList<double> DefaultChickenAnchor = new[] { 0.0, 0.428, -0.11, 1.06 };

    public static IReadOnlyList<ShellObject> Objects(DLCCatalog catalog)
    {
        var objs = new List<ShellObject>();
        if (catalog is null) return objs;
        foreach (var o in catalog.ShellObjects)
        {
            var piece = o.Pieces.Where(p => p.Dlc is not null).OrderBy(p => p.Lod).FirstOrDefault();
            if (piece?.Dlc is not { } dlc) continue;
            var url = Url(dlc);
            if (url is null) continue;
            var isChicken = o.AssetType == ShellSpec.Types.AssetType.Chicken;
            var anchor = o.Metadata.Count > 0
                ? o.Metadata.ToList()
                : (isChicken && !o.NoHats ? DefaultChickenAnchor : []);
            objs.Add(new ShellObject(o.Identifier ?? "", NullIfEmpty(o.Name), AssetTypeName(o.AssetType),
                url, NullIfEmpty(dlc.Checksum), anchor, o.NoHats));
        }
        return objs;
    }

    public static IReadOnlyList<ShellObject> Chickens(DLCCatalog catalog) =>
        Objects(catalog).Where(o => string.Equals(o.AssetType, "Chicken", StringComparison.OrdinalIgnoreCase)).ToList();

    public static IReadOnlyList<ShellObject> Hats(DLCCatalog catalog) =>
        Objects(catalog).Where(o => string.Equals(o.AssetType, "Hat", StringComparison.OrdinalIgnoreCase)).ToList();

    public static ShellObject? ObjectById(DLCCatalog catalog, string identifier) =>
        Objects(catalog).FirstOrDefault(o => string.Equals(o.Identifier, identifier, StringComparison.Ordinal));

    // A shell set (a coordinated reskin across asset types) or a decorator (a farm-wide cosmetic overlay).
    // Decorator=true marks a decorator (DLCCatalog.decorators) vs a set (DLCCatalog.shell_sets).
    public sealed record ShellSet(string Identifier, string? Name, bool Decorator, IReadOnlyList<Shell> Members);

    public static IReadOnlyList<ShellSet> Sets(DLCCatalog catalog) => BuildSets(catalog, catalog?.ShellSets, decorator: false);
    public static IReadOnlyList<ShellSet> Decorators(DLCCatalog catalog) => BuildSets(catalog, catalog?.Decorators, decorator: true);

    private static IReadOnlyList<ShellSet> BuildSets(DLCCatalog? catalog, IEnumerable<ShellSetSpec>? specs, bool decorator)
    {
        var result = new List<ShellSet>();
        if (catalog is null || specs is null) return result;
        var bySet = FromCatalog(catalog)
            .Where(s => !string.IsNullOrEmpty(s.SetIdentifier))
            .GroupBy(s => s.SetIdentifier!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Shell>)g.ToList(), StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            var id = spec.Identifier ?? "";
            if (id.Length == 0) continue;
            var members = bySet.TryGetValue(id, out var m) ? m : [];
            result.Add(new ShellSet(id, NullIfEmpty(spec.Name), decorator, members));
        }
        return result;
    }

    private static string? Url(DLCItem dlc)
    {
        if (!string.IsNullOrEmpty(dlc.Url)) return dlc.Url;
        if (string.IsNullOrEmpty(dlc.Directory) || string.IsNullOrEmpty(dlc.Name)) return null;
        var ext = string.IsNullOrEmpty(dlc.Ext) ? "rpoz" : dlc.Ext;
        return $"{CdnBase}/{dlc.Directory}/{dlc.Name}.{ext}";
    }

    private static string AssetTypeName(ShellSpec.Types.AssetType t) => t.ToString();
    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}

using Ei;

namespace EggIncognito.Services.ProtoExtract;

// Resolves the CDN mesh URL for the 4 orbital ships (Galeggtica, Defihent/Chickfiant, Voyegger, Henerprise)
// that are NOT bundled in the app. Their mesh is a DLCItem inside the DLCCatalog the game returns from
// ei/get_config (ConfigResponse.dlc_catalog). The ids/hashes appear nowhere public, so this reads them
// straight from a live (or captured) DLCCatalog. URL building mirrors carpetsage/egg's shell-company tool:
//   DLCItem.url, if set, is the absolute URL; otherwise https://www.auxbrain.com/dlc/{directory}/{name}.{ext}
// A 3D mesh DLCItem has ext "rpoz" (zlib-wrapped .rpo). The resolved bytes feed RpoMeshDecoder unchanged.
//
// Match: an afx ship asset name (afx_ship_galeggtica, ...) -> the DLCItem(s) whose name STARTS WITH it. Ship
// meshes ride DLCItems nested in ShellSpec.pieces[].dlc / ShellObjectSpec.pieces[].dlc (LOD pieces), and may
// also appear flat in DLCCatalog.items. ShellObject pieces carry a LOD level; a ship may ship several LODs
// (afx_ship_galeggtica, afx_ship_galeggtica_lod1, ...). We prefer the lowest LOD (highest detail) per ship.
public static class ShipShellResolver
{
    private const string CdnBase = "https://www.auxbrain.com/dlc";

    public sealed record ShipShell(string AfxName, string MatchedItemName, string Url, bool Compressed, string? Checksum, int Lod);

    // Walks a DLCCatalog and returns the best (lowest-LOD) resolvable mesh URL for each afx ship name. Names
    // with no matching DLCItem are absent from the result (caller reports them as still-missing). Never throws.
    public static IReadOnlyList<ShipShell> Resolve(DLCCatalog catalog, IEnumerable<string> afxShipNames)
    {
        var wanted = afxShipNames.ToList();
        var best = new Dictionary<string, ShipShell>(StringComparer.OrdinalIgnoreCase);
        if (catalog is null) return [];

        foreach (var (item, lod) in AllDlcItems(catalog))
        {
            if (item.Name is null) continue;
            var afx = wanted.FirstOrDefault(w => item.Name.StartsWith(w, StringComparison.OrdinalIgnoreCase));
            if (afx is null) continue;
            var url = BuildUrl(item);
            if (url is null) continue;
            // keep the lowest LOD seen for this ship (highest detail); ties keep the first.
            if (best.TryGetValue(afx, out var cur) && cur.Lod <= lod) continue;
            best[afx] = new ShipShell(afx, item.Name, url, item.Compressed, NullIfEmpty(item.Checksum), lod);
        }
        return best.Values.ToList();
    }

    // Every DLCItem reachable in the catalog, paired with its LOD (0 when not a LOD piece): the flat items
    // list + each shell piece's dlc + alt_assets + each shell-object LOD piece's dlc.
    private static IEnumerable<(DLCItem Item, int Lod)> AllDlcItems(DLCCatalog catalog)
    {
        foreach (var i in catalog.Items) yield return (i, 0);

        foreach (var shell in catalog.Shells)
        {
            if (shell.PrimaryPiece?.Dlc is { } pp) yield return (pp, 0);
            foreach (var piece in shell.Pieces)
                if (piece.Dlc is { } d) yield return (d, 0);
            foreach (var alt in shell.AltAssets) yield return (alt, 0);
        }

        foreach (var obj in catalog.ShellObjects)
            foreach (var piece in obj.Pieces)
                if (piece.Dlc is { } d) yield return (d, (int)piece.Lod);
    }

    // url wins when present (absolute). Otherwise compose from directory + name + ext, the carpetsage pattern.
    private static string? BuildUrl(DLCItem item)
    {
        if (!string.IsNullOrEmpty(item.Url)) return item.Url;
        if (string.IsNullOrEmpty(item.Directory) || string.IsNullOrEmpty(item.Name)) return null;
        var ext = string.IsNullOrEmpty(item.Ext) ? "rpoz" : item.Ext;
        return $"{CdnBase}/{item.Directory}/{item.Name}.{ext}";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}

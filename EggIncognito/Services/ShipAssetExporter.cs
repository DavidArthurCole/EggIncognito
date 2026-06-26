using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

// Turns a decoded mesh set (RpoAssetExtractor output) into the public asset-repo layout EggLedger consumes:
// ships/<EnumName>.glb (one per Spaceship enum ship, renamed from the rpos/ stem via ShipNameMap) plus a
// manifest.json keyed by enum name with bbox + sha256 per ship. Non-ship assets (habs, pipes, props) and
// CDN-only ships are dropped. This is the permanent export stage; the web endpoints drive it against a
// device-pulled or uploaded archive, and CI hits those endpoints. No throwaway scripting.
public static class ShipAssetExporter
{
    // The manifest contract (matches docs/handoff + the asset-repo README). version is the contract version,
    // generatedFromBuild is the game build the meshes came from (caller supplies; null when unknown).
    public sealed record Manifest(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("generatedFromBuild")] string? GeneratedFromBuild,
        [property: JsonPropertyName("ships")] IReadOnlyDictionary<string, ShipEntry> Ships);

    public sealed record ShipEntry(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("bbox")] BBox Bbox);

    public sealed record BBox(
        [property: JsonPropertyName("min")] float[] Min,
        [property: JsonPropertyName("max")] float[] Max);

    // One exported ship: enum name, the .glb bytes (renamed), and its manifest entry. Bytes are kept so the
    // caller can write them to disk OR return them base64 over HTTP without re-decoding.
    public sealed record Exported(string EnumName, byte[] Glb, ShipEntry Entry);

    public sealed record Result(Manifest Manifest, IReadOnlyList<Exported> Ships, IReadOnlyList<string> SkippedShips);

    // Filters the decoded assets to bundled ships, renames to <EnumName>.glb, builds the manifest. SkippedShips
    // = enum ships with no bundled mesh in this archive (the 4 CDN ships, or anything missing), so the caller
    // can report coverage instead of silently shipping a partial set.
    // animate, when set, bakes a glTF animation (e.g. SpinY) into each ship .glb before hashing/export, so
    // consumers get a self-playing spin without client-side animation code. null = static geometry only.
    public static Result Build(RpoAssetExtractor.ExtractResult extract, string? generatedFromBuild,
        EggIncognito.Services.Assets.GltfAnimator.Options? animate = null)
    {
        var exported = new List<Exported>();
        var entries = new Dictionary<string, ShipEntry>(StringComparer.Ordinal);

        foreach (var asset in extract.Assets)
        {
            if (!asset.Decode.Ok) continue;
            var enumName = ShipNameMap.EnumNameForStem(asset.Key);
            if (enumName is null) continue; // not a ship (or a CDN-only ship): drop

            var glb = asset.Decode.Glb!;
            // Optionally bake the animation in. A failed animate keeps the static glb rather than dropping
            // the ship, so a toolkit hiccup never silently loses a mesh.
            if (animate is not null)
            {
                var anim = EggIncognito.Services.Assets.GltfAnimator.Animate(glb, animate);
                if (anim.Ok) glb = anim.Glb!;
            }
            var b = asset.Decode.Bounds!;
            var entry = new ShipEntry(
                File: $"ships/{enumName}.glb",
                Sha256: Convert.ToHexStringLower(SHA256.HashData(glb)),
                Bbox: new BBox([b.Min.X, b.Min.Y, b.Min.Z], [b.Max.X, b.Max.Y, b.Max.Z]));

            exported.Add(new Exported(enumName, glb, entry));
            entries[enumName] = entry;
        }

        var present = entries.Keys.ToHashSet(StringComparer.Ordinal);
        var skipped = ShipNameMap.All.Select(s => s.EnumName).Where(n => !present.Contains(n)).ToList();

        var manifest = new Manifest("1", generatedFromBuild, entries);
        return new Result(manifest, exported, skipped);
    }

    // Writes the export to disk in the asset-repo layout: <outputDir>/ships/<EnumName>.glb + manifest.json.
    // Used by the export endpoint when an output directory is configured (the CI artifact path). Overwrites.
    public static async Task WriteToAsync(Result result, string outputDir, CancellationToken ct)
    {
        var shipsDir = Path.Combine(outputDir, "ships");
        Directory.CreateDirectory(shipsDir);
        foreach (var s in result.Ships)
            await File.WriteAllBytesAsync(Path.Combine(shipsDir, $"{s.EnumName}.glb"), s.Glb, ct);

        var json = JsonSerializer.SerializeToUtf8Bytes(result.Manifest,
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllBytesAsync(Path.Combine(outputDir, "manifest.json"), json, ct);
    }
}

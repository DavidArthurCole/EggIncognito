using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;


public static class ShipAssetExporter {

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

    public sealed record Exported(string EnumName, byte[] Glb, ShipEntry Entry);

    public sealed record Result(Manifest Manifest, IReadOnlyList<Exported> Ships, IReadOnlyList<string> SkippedShips);



    public static Result Build(RpoAssetExtractor.ExtractResult extract, string? generatedFromBuild,
        EggIncognito.Services.Assets.GltfAnimator.Options? animate = null) {
        var exported = new List<Exported>();
        var entries = new Dictionary<string, ShipEntry>(StringComparer.Ordinal);

        foreach (var asset in extract.Assets) {
            if (!asset.Decode.Ok) continue;
            var enumName = ShipNameMap.EnumNameForStem(asset.Key);
            if (enumName is null) continue;

            var glb = asset.Decode.Glb!;

            if (animate is not null) {
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


    public static async Task WriteToAsync(Result result, string outputDir, CancellationToken ct) {
        var shipsDir = Path.Combine(outputDir, "ships");
        Directory.CreateDirectory(shipsDir);
        foreach (var s in result.Ships)
            await File.WriteAllBytesAsync(Path.Combine(shipsDir, $"{s.EnumName}.glb"), s.Glb, ct);

        var json = JsonSerializer.SerializeToUtf8Bytes(result.Manifest,
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllBytesAsync(Path.Combine(outputDir, "manifest.json"), json, ct);
    }
}

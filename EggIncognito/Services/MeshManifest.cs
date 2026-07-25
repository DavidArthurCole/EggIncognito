using System.Security.Cryptography;
using EggIncognito.Services.Assets;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

public static class MeshManifest {
    public static object From(RpoAssetExtractor.ExtractResult r) {
        var ships = r.Assets.Where(a => a.Decode.Ok).Select(a => {
            byte[] glb = a.Decode.Glb!;
            var b = a.Decode.Bounds!;
            return new {
                key = a.Key,
                source = a.SourceEntry,
                vertexCount = a.Decode.VertexCount,
                indexCount = a.Decode.IndexCount,
                hasEmission = a.Decode.HasEmission,
                bbox = new { min = new[] { b.Min.X, b.Min.Y, b.Min.Z }, max = new[] { b.Max.X, b.Max.Y, b.Max.Z } },
                sha256 = Convert.ToHexStringLower(SHA256.HashData(glb)),
                glbBase64 = Convert.ToBase64String(glb)
            };
        }).ToList();

        var failed = r.Assets.Where(a => !a.Decode.Ok)
            .Select(a => new { key = a.Key, source = a.SourceEntry, diagnostics = a.Decode.Diagnostics }).ToList();

        return new { ok = r.Ok, diagnostics = r.Diagnostics, count = ships.Count, ships, failed };
    }


    public static object Ships(RpoAssetExtractor.ExtractResult r, string? build, bool wroteToDisk, string? outputDir,
        GltfAnimator.Options? animate = null) {
        var export = ShipAssetExporter.Build(r, build, animate);
        var ships = export.Ships.Select(s => new {
            enumName = s.EnumName,
            file = s.Entry.File,
            sha256 = s.Entry.Sha256,
            bbox = new { min = s.Entry.Bbox.Min, max = s.Entry.Bbox.Max },
            glbBase64 = Convert.ToBase64String(s.Glb)
        }).ToList();

        return new {
            ok = ships.Count > 0,
            count = ships.Count,
            manifest = export.Manifest,
            ships,
            skipped = export.SkippedShips,
            wroteToDisk,
            outputDir = wroteToDisk ? outputDir : null
        };
    }
}

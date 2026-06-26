using System.Security.Cryptography;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

// Shapes a decoded mesh set into the consumer manifest both mesh endpoints return (POST /api/tools/
// extract-meshes from an uploaded archive, POST /api/devices/{id}/pull-meshes from a live device). One
// place so the contract EggLedger consumes never drifts between the two paths: per-ship key, bbox, vertex/
// index counts, emission flag, sha256 over the glb, and the glb bytes base64-encoded.
public static class MeshManifest
{
    public static object From(RpoAssetExtractor.ExtractResult r)
    {
        var ships = r.Assets.Where(a => a.Decode.Ok).Select(a =>
        {
            var glb = a.Decode.Glb!;
            var b = a.Decode.Bounds!;
            return new
            {
                key = a.Key,
                source = a.SourceEntry,
                vertexCount = a.Decode.VertexCount,
                indexCount = a.Decode.IndexCount,
                hasEmission = a.Decode.HasEmission,
                bbox = new { min = new[] { b.Min.X, b.Min.Y, b.Min.Z }, max = new[] { b.Max.X, b.Max.Y, b.Max.Z } },
                sha256 = Convert.ToHexStringLower(SHA256.HashData(glb)),
                glbBase64 = Convert.ToBase64String(glb),
            };
        }).ToList();

        var failed = r.Assets.Where(a => !a.Decode.Ok)
            .Select(a => new { key = a.Key, source = a.SourceEntry, diagnostics = a.Decode.Diagnostics }).ToList();

        return new { ok = r.Ok, diagnostics = r.Diagnostics, count = ships.Count, ships, failed };
    }

    // Ship-export shape: filters the decoded set to Spaceship enum ships (via ShipNameMap), renames to
    // <EnumName>.glb, and returns the manifest + per-ship glb base64 + the enum ships still missing a mesh
    // (the CDN-only ships). Used by the /export-ships path so EggLedger gets enum-keyed assets directly.
    public static object Ships(RpoAssetExtractor.ExtractResult r, string? build, bool wroteToDisk, string? outputDir)
    {
        var export = ShipAssetExporter.Build(r, build);
        var ships = export.Ships.Select(s => new
        {
            enumName = s.EnumName,
            file = s.Entry.File,
            sha256 = s.Entry.Sha256,
            bbox = new { min = s.Entry.Bbox.Min, max = s.Entry.Bbox.Max },
            glbBase64 = Convert.ToBase64String(s.Glb),
        }).ToList();

        return new
        {
            ok = ships.Count > 0,
            count = ships.Count,
            manifest = export.Manifest,
            ships,
            skipped = export.SkippedShips, // enum ships with no bundled mesh (CDN-only / absent)
            wroteToDisk,
            outputDir = wroteToDisk ? outputDir : null,
        };
    }
}

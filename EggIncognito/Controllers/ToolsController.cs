using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Read-only tooling, hosted-safe (no writes): Postman export, blob decode, endpoint status. The read
// limiter covers decode/diagnose, which parse arbitrary client-supplied base64 protobuf.
[ApiController]
[Route("api/tools")]
[EnableRateLimiting("read")]
public sealed class ToolsController(IConfiguration config, IProtoReflection reflection) : ControllerBase
{
    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string YamlPath => Path.Combine(Root, "RouteMap", "routes.yaml");
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");
    private string CapturePath => config["CapturePath"] ?? Path.Combine(Root, "captures");

    // Latest live app version harvested from captured BasicRequestInfo for a platform. This is the
    // authoritative iOS clientVersion + auxbrain build (the static IPA binary cannot give them); the
    // Proto Registry Analyze form uses it to backfill clientVersion + build. Empty 200 when none seen
    // (e.g. no capture has run on this host, or only the other platform was observed).
    [HttpGet("live-version")]
    public IActionResult LiveVersion([FromQuery] string platform = "ios")
    {
        var v = new Capture.LiveVersionStore(CapturePath).Latest(platform);
        if (v is null) return Ok(new { found = false });
        return Ok(new { found = true, v.Platform, v.Version, v.Build, v.ClientVersion, v.LastSeen });
    }

    [HttpGet("postman-collection")]
    public IActionResult PostmanCollection()
    {
        var json = Services.PostmanCollection.BuildJson(YamlPath);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "EggIncognito.postman_collection.json");
    }

    public sealed record DecodeRequest(string Base64);

    [HttpPost("decode")]
    public IActionResult Decode([FromBody] DecodeRequest body)
    {
        var r = BlobDecoder.Decode(body.Base64 ?? "");
        return Ok(new { type = r.Type, json = r.Json, wrapped = r.Wrapped, confidence = r.Confidence });
    }

    [HttpGet("endpoint-status")]
    public IActionResult Status()
    {
        var r = EndpointStatus.Classify(YamlPath, DefaultsDir);
        return Ok(new { ok = r.Ok, empty = r.Empty, missing = r.Missing });
    }

    public sealed record ExtractIosProtoRequest(string BinaryBase64);

    // Read-only, hosted-safe: carve the embedded FileDescriptorProto out of a decrypted iOS Mach-O and
    // return the reconstructed .proto + SHA + message list. STATIC binary read; the binary is never
    // executed. Large binaries (50-90MB) base64 poorly over HTTP - the __extract-ios-proto CLI and the
    // multipart drop-zone are preferred for those; this JSON form stays for small inputs + parity.
    [HttpPost("extract-ios-proto")]
    public IActionResult ExtractIosProto([FromBody] ExtractIosProtoRequest body)
    {
        byte[] macho;
        try { macho = Convert.FromBase64String(body.BinaryBase64 ?? ""); }
        catch { return Ok(new { ok = false, diagnostics = "input is not valid base64" }); }
        return ExtractResultJson(Services.ProtoExtract.DescriptorProtoCarver.Extract(macho));
    }

    // Multipart drop-zone endpoint: a decrypted iOS Mach-O OR an Android APK, auto-detected by content.
    // Public read tool (no writes). The browser posts the file directly (not over the SignalR circuit),
    // so 50-90MB uploads do not choke the Blazor channel. STATIC read; never executed.
    [HttpPost("extract-proto")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExtractProto(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
        // Pre-size to the known length: an unsized MemoryStream doubles its buffer as the ~80MB upload
        // grows, recopying the whole thing on every resize. Carving needs a contiguous array, so read
        // straight into one sized to file.Length instead of stream-then-ToArray (which double-buffers).
        var bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        // APK + IPA are both zips (PK\x03\x04); the archive extractor locates the native binary entry
        // inside (Android .so / iOS Payload/*.app exec) and carves it. A non-zip is a raw binary.
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var r = isZip
            ? Services.ProtoExtract.ArchiveProtoExtractor.Extract(bytes)
            : Services.ProtoExtract.DescriptorProtoCarver.Extract(bytes);
        return ExtractResultJson(r);
    }

    // Multipart drop-zone: an Android APK or iOS IPA (both zips). Decodes every .rpo/.rpoz ship mesh found
    // inside to glTF 2.0 (.glb) and returns a manifest-shaped result: per-mesh key (the base filename, which
    // the asset pipeline maps to the MissionInfo.Spaceship enum), bbox, vertex/index counts, a sha256 over
    // the glb, an emission flag, and the glb bytes themselves base64-encoded. Public read tool, no writes;
    // STATIC parse, never executed. EI's per-vertex emission is preserved as the glTF COLOR_0 attribute.
    [HttpPost("extract-meshes")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExtractMeshes(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
        var bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        var r = Services.ProtoExtract.RpoAssetExtractor.Extract(bytes);
        return Ok(Services.MeshManifest.From(r));
    }

    // Multipart drop-zone, ship-export variant: decodes the archive's meshes, filters to the Spaceship enum
    // ships (via ShipNameMap), renames each to <EnumName>.glb, and returns the asset-repo manifest + per-ship
    // glb base64 + the enum ships still missing a bundled mesh (the CDN-only ships). When ShipAssets:OutputDir
    // is configured AND write=true (and writes are enabled), also writes ships/<EnumName>.glb + manifest.json
    // to that dir - the CI artifact path. build= stamps the manifest's generatedFromBuild.
    [HttpPost("export-ships")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExportShips(IFormFile file, [FromQuery] string? build, [FromQuery] bool write, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
        var bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        var r = Services.ProtoExtract.RpoAssetExtractor.Extract(bytes);
        var (wrote, dir) = await MaybeWriteAsync(r, build, write, ct);
        return Ok(Services.MeshManifest.Ships(r, build, wrote, dir));
    }

    // Writes the ship export to ShipAssets:OutputDir when configured + requested + writes enabled. Returns
    // (wroteToDisk, dir). Gated by CanWrite so a Hosted instance never writes to shared disk.
    private async Task<(bool, string?)> MaybeWriteAsync(
        Services.ProtoExtract.RpoAssetExtractor.ExtractResult r, string? build, bool write, CancellationToken ct)
    {
        if (!write) return (false, null);
        var dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return (false, null);
        var export = Services.ShipAssetExporter.Build(r, build);
        if (export.Ships.Count == 0) return (false, null);
        await Services.ShipAssetExporter.WriteToAsync(export, dir, ct);
        return (true, dir);
    }

    private IActionResult ExtractResultJson(Services.ProtoExtract.DescriptorProtoCarver.ExtractResult r) =>
        Ok(new { ok = r.Ok, proto = r.Proto, diagnostics = r.Diagnostics, protoSha = r.ProtoSha,
            messages = r.Messages, appVersion = r.AppVersion, build = r.Build });

    public sealed record DiagnoseRequest(string Base64, string? RootType);

    // Read-only, hosted-safe: structural + (optional) schema-aware wire diagnosis of a base64 proto blob.
    // No egress, no writes. RootType omitted => structural-only (no field-name resolution / mismatch flags).
    [HttpPost("diagnose")]
    public IActionResult Diagnose([FromBody] DiagnoseRequest body)
    {
        byte[] bytes;
        try { bytes = ProtoFraming.FromBase64Loose(body.Base64 ?? ""); }
        catch { return Ok(new { error = "input is not valid base64" }); }

        // Diagnose the inner payload when the blob is a wrapped AuthenticatedMessage, else the bytes as-is,
        // so a corrupt backup (wrapped on the wire) is walked at the message level the schema describes.
        var inner = ProtoFraming.TryUnwrap(bytes) ?? bytes;
        var result = WireForensics.Diagnose(inner, body.RootType, reflection);
        return Ok(result);
    }
}

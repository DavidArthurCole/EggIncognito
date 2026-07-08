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

    // Latest live app version harvested from captured BasicRequestInfo for a platform: the authoritative
    // iOS clientVersion and auxbrain build, since the static IPA binary cannot give them. Empty 200 when
    // none seen.
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

    // Carve the embedded FileDescriptorProto out of a decrypted iOS Mach-O and return the reconstructed
    // .proto, SHA, and message list. Static binary read; large binaries should use the multipart
    // drop-zone instead, this JSON form is for small inputs.
    [HttpPost("extract-ios-proto")]
    public IActionResult ExtractIosProto([FromBody] ExtractIosProtoRequest body)
    {
        byte[] macho;
        try { macho = Convert.FromBase64String(body.BinaryBase64 ?? ""); }
        catch { return Ok(new { ok = false, diagnostics = "input is not valid base64" }); }
        return ExtractResultJson(Services.ProtoExtract.DescriptorProtoCarver.Extract(macho));
    }

    // Multipart drop-zone endpoint: a decrypted iOS Mach-O or an Android APK, auto-detected by content.
    // The browser posts the file directly (not over the SignalR circuit), so large uploads do not choke
    // the Blazor channel.
    [HttpPost("extract-proto")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExtractProto(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
        // Read straight into an array sized to file.Length: carving needs a contiguous array, and an
        // unsized MemoryStream would double-buffer as a large upload grows.
        var bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        // APK + IPA are both zips (PK\x03\x04); the archive extractor locates and carves the native
        // binary entry inside. A non-zip is a raw binary.
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var r = isZip
            ? Services.ProtoExtract.ArchiveProtoExtractor.Extract(bytes)
            : Services.ProtoExtract.DescriptorProtoCarver.Extract(bytes);
        return ExtractResultJson(r);
    }

    // Multipart drop-zone: an Android APK or iOS IPA (both zips). Decodes every .rpo/.rpoz ship mesh found
    // inside to glTF 2.0 (.glb) and returns a manifest-shaped result: per-mesh key, bbox, vertex/index
    // counts, sha256, emission flag, and the glb bytes base64-encoded.
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
    // ships (via ShipNameMap), renames each to <EnumName>.glb, and returns the asset-repo manifest plus
    // per-ship glb base64. With ShipAssets:OutputDir configured and write=true, also writes
    // ships/<EnumName>.glb and manifest.json to that dir.
    [HttpPost("export-ships")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExportShips(IFormFile file, [FromQuery] string? build, [FromQuery] bool write,
        [FromQuery] string? animate, [FromQuery] float seconds, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
        var bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        var anim = string.IsNullOrEmpty(animate) ? null
            : new Services.Assets.GltfAnimator.Options(Services.Assets.GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);

        var r = Services.ProtoExtract.RpoAssetExtractor.Extract(bytes);
        var (wrote, dir) = await MaybeWriteAsync(r, build, write, anim, ct);
        return Ok(Services.MeshManifest.Ships(r, build, wrote, dir, anim));
    }

    // Writes the ship export to ShipAssets:OutputDir when configured, requested, and writes are enabled.
    // Returns (wroteToDisk, dir).
    private async Task<(bool, string?)> MaybeWriteAsync(
        Services.ProtoExtract.RpoAssetExtractor.ExtractResult r, string? build, bool write,
        Services.Assets.GltfAnimator.Options? animate, CancellationToken ct)
    {
        if (!write) return (false, null);
        var dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return (false, null);
        var export = Services.ShipAssetExporter.Build(r, build, animate);
        if (export.Ships.Count == 0) return (false, null);
        await Services.ShipAssetExporter.WriteToAsync(export, dir, ct);
        return (true, dir);
    }

    // Multipart drop-zone: a .glb in, an animated .glb out. Bakes a glTF rotation/hover animation into the
    // model (the bundled ship meshes are static). kind = SpinY (default) | SpinZ | HoverSpin; seconds =
    // clip length.
    [HttpPost("animate-glb")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> AnimateGlb(IFormFile file, [FromQuery] string? kind, [FromQuery] float seconds, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { ok = false, diagnostics = "no file uploaded" });
        var bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        var opts = new Services.Assets.GltfAnimator.Options(
            Services.Assets.GltfAnimator.ParseKind(kind), seconds > 0 ? seconds : 6f);
        var r = Services.Assets.GltfAnimator.Animate(bytes, opts);
        if (!r.Ok) return Ok(new { ok = false, diagnostics = r.Diagnostics });

        var name = Path.GetFileNameWithoutExtension(file.FileName) is { Length: > 0 } n ? n : "model";
        return File(r.Glb!, "model/gltf-binary", $"{name}.{r.AnimationName}.glb");
    }

    private IActionResult ExtractResultJson(Services.ProtoExtract.DescriptorProtoCarver.ExtractResult r) =>
        Ok(new { ok = r.Ok, proto = r.Proto, diagnostics = r.Diagnostics, protoSha = r.ProtoSha,
            messages = r.Messages, appVersion = r.AppVersion, build = r.Build });

    public sealed record DiagnoseRequest(string Base64, string? RootType);

    // Structural plus optional schema-aware wire diagnosis of a base64 proto blob. RootType omitted means
    // structural-only, no field-name resolution or mismatch flags.
    [HttpPost("diagnose")]
    public IActionResult Diagnose([FromBody] DiagnoseRequest body)
    {
        byte[] bytes;
        try { bytes = ProtoFraming.FromBase64Loose(body.Base64 ?? ""); }
        catch { return Ok(new { error = "input is not valid base64" }); }

        // Diagnose the inner payload when the blob is a wrapped AuthenticatedMessage, else the bytes as-is.
        var inner = ProtoFraming.TryUnwrap(bytes) ?? bytes;
        var result = WireForensics.Diagnose(inner, body.RootType, reflection);
        return Ok(result);
    }
}

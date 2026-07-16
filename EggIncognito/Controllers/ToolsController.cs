using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/tools")]
[EnableRateLimiting("read")]
public sealed class ToolsController(IConfiguration config, IProtoReflection reflection) : ControllerBase
{
    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string YamlPath => Path.Combine(Root, "RouteMap", "routes.yaml");
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");
    private string CapturePath => config["CapturePath"] ?? Path.Combine(Root, "captures");

   
   
   
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

   
   
   
    [HttpPost("extract-ios-proto")]
    public IActionResult ExtractIosProto([FromBody] ExtractIosProtoRequest body)
    {
        byte[] macho;
        try { macho = Convert.FromBase64String(body.BinaryBase64 ?? ""); }
        catch { return Ok(new { ok = false, diagnostics = "input is not valid base64" }); }
        return ExtractResultJson(Services.ProtoExtract.DescriptorProtoCarver.Extract(macho));
    }

   
   
   
    [HttpPost("extract-proto")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExtractProto(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
       
       
        var bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

       
       
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var r = isZip
            ? Services.ProtoExtract.ArchiveProtoExtractor.Extract(bytes)
            : Services.ProtoExtract.DescriptorProtoCarver.Extract(bytes);
        return ExtractResultJson(r);
    }

   
   
   
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

   
   
    [HttpPost("diagnose")]
    public IActionResult Diagnose([FromBody] DiagnoseRequest body)
    {
        byte[] bytes;
        try { bytes = ProtoFraming.FromBase64Loose(body.Base64 ?? ""); }
        catch { return Ok(new { error = "input is not valid base64" }); }

       
        var inner = ProtoFraming.TryUnwrap(bytes) ?? bytes;
        var result = WireForensics.Diagnose(inner, body.RootType, reflection);
        return Ok(result);
    }
}

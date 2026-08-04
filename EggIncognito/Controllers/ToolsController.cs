using System.Data.Common;
using System.Text;
using EggIncognito.Capture;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Auth;
using EggIncognito.Services.ProtoExtract;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/tools")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class ToolsController(IConfiguration config, IProtoReflection reflection) : ControllerBase {
    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string YamlPath => Path.Combine(Root, "RouteMap", "routes.yaml");
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");
    private string CapturePath => config["CapturePath"] ?? Path.Combine(Root, "captures");


    [HttpGet("live-version")]
    public IActionResult LiveVersion([FromQuery] string platform = "ios") {
        var v = new LiveVersionStore(CapturePath).Latest(platform);
        if (v is null) return Ok(new { found = false });
        return Ok(new { found = true, v.Platform, v.Version, v.Build, v.ClientVersion, v.LastSeen });
    }

    [HttpGet("postman-collection")]
    public IActionResult PostmanCollection() {
        string json = Services.PostmanBundle.BuildJson(YamlPath);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "EggIncognito.postman_collection.json");
    }

    [HttpPost("decode")]
    public IActionResult Decode([FromBody] DecodeRequest body) {
        var r = BlobDecoder.Decode(body.Base64 ?? "");
        return Ok(new { type = r.Type, json = r.Json, wrapped = r.Wrapped, confidence = r.Confidence });
    }

    [HttpGet("endpoint-status")]
    public IActionResult Status() {
        var r = EndpointStatus.Classify(YamlPath, DefaultsDir);
        return Ok(new { ok = r.Ok, empty = r.Empty, missing = r.Missing });
    }

    [HttpGet("boost-costs")]
    public IActionResult BoostCosts() {
        string path = Path.Combine(DefaultsDir, "ei", "get_config.json");
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "no get_config capture" });
        try {
            string json = System.IO.File.ReadAllText(path);
            var costs = BoostCostExtractor.FromConfigJson(json);
            return Ok(new {
                count = costs.Count,
                costs = costs.Select(kv => new { boostId = kv.Key, kv.Value.Price, kv.Value.TokenPrice, kv.Value.SeRequired })
            });
        } catch (Exception ex) {
            return Ok(new { count = 0, error = ex.Message });
        }
    }

    [HttpGet("colleggtibles")]
    public IActionResult Colleggtibles() {
        string path = Path.Combine(DefaultsDir, "ei", "get_periodicals.json");
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "no get_periodicals capture" });
        try {
            string json = System.IO.File.ReadAllText(path);
            var extract = ColleggtibleExtractor.FromPeriodicalsJson(json);
            return Ok(new {
                count = extract.Eggs.Count,
                eggs = extract.Eggs.Select(e => new { e.Identifier, e.Dimension, e.TierValues }),
                contractEggMap = extract.ContractEggMap
            });
        } catch (Exception ex) {
            return Ok(new { count = 0, error = ex.Message });
        }
    }


    [HttpPost("extract-ios-proto")]
    public IActionResult ExtractIosProto([FromBody] ExtractIosProtoRequest body) {
        byte[] macho;
        try {
            macho = Convert.FromBase64String(body.BinaryBase64 ?? "");
        } catch {
            return Ok(new { ok = false, diagnostics = "input is not valid base64" });
        }

        return ExtractResultJson(DescriptorProtoCarver.Extract(macho));
    }


    [HttpPost("extract-proto")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExtractProto(IFormFile binary, IFormFile? meta, [FromForm] string? fileName,
        CancellationToken ct) {
        if (binary is null || binary.Length == 0) return Ok(new { ok = false, diagnostics = "no binary uploaded" });

        byte[] bin = await ReadFormFileAsync(binary, ct);
        byte[]? metaBytes = meta is { Length: > 0 } ? await ReadFormFileAsync(meta, ct) : null;

        var r = DescriptorProtoCarver.Extract(bin);
        if (r.Ok) {
            (string? appVersion, string? build) = AppMetaReader.Read(metaBytes);
            r = r with { AppVersion = appVersion, Build = build };
            await RecordAnalyzedAsync(bin, r, fileName ?? binary.FileName, ct);
        }

        return ExtractResultJson(r, AnalyzedFileStore.Sha256Hex(bin));
    }

    private static async Task<byte[]> ReadFormFileAsync(IFormFile file, CancellationToken ct) {
        byte[] bytes = new byte[file.Length];
        using var dest = new MemoryStream(bytes);
        await file.CopyToAsync(dest, ct);
        return bytes;
    }

    private async Task RecordAnalyzedAsync(byte[] bytes, DescriptorProtoCarver.ExtractResult r, string? fileName,
        CancellationToken ct) {
        var store = HttpContext.RequestServices.GetService<AnalyzedFileStore>();
        if (store is null) return;
        try {
            await store.RecordAsync(new AnalyzedFileStore.Entry(
                AnalyzedFileStore.Sha256Hex(bytes), "analyze", null, r.ProtoSha, r.AppVersion, r.Build,
                r.ClientVersion?.ToString(), fileName), ct);
        } catch (DbException) {
        }
    }


    [HttpPost("extract-meshes")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExtractMeshes(IFormFile file, CancellationToken ct) {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
        byte[] bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        var r = RpoAssetExtractor.Extract(bytes);
        return Ok(MeshManifest.From(r));
    }


    [HttpPost("export-ships")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ExportShips(IFormFile file, [FromQuery] string? build, [FromQuery] bool write,
        [FromQuery] string? animate, [FromQuery] float seconds, CancellationToken ct) {
        if (file is null || file.Length == 0) return Ok(new { ok = false, diagnostics = "no file uploaded" });
        byte[] bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        var anim = string.IsNullOrEmpty(animate)
            ? null
            : new GltfAnimator.Options(GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);

        var r = RpoAssetExtractor.Extract(bytes);
        (bool wrote, string? dir) = await MaybeWriteAsync(r, build, write, anim, ct);
        return Ok(MeshManifest.Ships(r, build, wrote, dir, anim));
    }


    private async Task<(bool, string?)> MaybeWriteAsync(
        RpoAssetExtractor.ExtractResult r, string? build, bool write,
        GltfAnimator.Options? animate, CancellationToken ct) {
        if (!write) return (false, null);
        string? dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return (false, null);
        var export = ShipAssetExporter.Build(r, build, animate);
        if (export.Ships.Count == 0) return (false, null);
        await ShipAssetExporter.WriteToAsync(export, dir, ct);
        return (true, dir);
    }


    [HttpPost("animate-glb")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> AnimateGlb(IFormFile file, [FromQuery] string? kind, [FromQuery] float seconds,
        CancellationToken ct) {
        if (file is null || file.Length == 0) return BadRequest(new { ok = false, diagnostics = "no file uploaded" });
        byte[] bytes = new byte[file.Length];
        using (var dest = new MemoryStream(bytes)) await file.CopyToAsync(dest, ct);

        var opts = new GltfAnimator.Options(
            GltfAnimator.ParseKind(kind), seconds > 0 ? seconds : 6f);
        var r = GltfAnimator.Animate(bytes, opts);
        if (!r.Ok) return Ok(new { ok = false, diagnostics = r.Diagnostics });

        string name = Path.GetFileNameWithoutExtension(file.FileName) is { Length: > 0 } n ? n : "model";
        return File(r.Glb!, "model/gltf-binary", $"{name}.{r.AnimationName}.glb");
    }

    private OkObjectResult ExtractResultJson(DescriptorProtoCarver.ExtractResult r, string? fileSha = null) =>
        Ok(new {
            ok = r.Ok,
            proto = r.Proto,
            diagnostics = r.Diagnostics,
            protoSha = r.ProtoSha,
            messages = r.Messages,
            appVersion = r.AppVersion,
            build = r.Build,
            clientVersion = r.ClientVersion,
            fileSha
        });


    [HttpPost("diagnose")]
    public IActionResult Diagnose([FromBody] DiagnoseRequest body) {
        byte[] bytes;
        try {
            bytes = ProtoFraming.FromBase64Loose(body.Base64 ?? "");
        } catch {
            return Ok(new { error = "input is not valid base64" });
        }


        byte[] inner = ProtoFraming.TryUnwrap(bytes) ?? bytes;
        var result = WireForensics.Diagnose(inner, body.RootType, reflection);
        return Ok(result);
    }

    public sealed record DecodeRequest(string Base64);

    public sealed record ExtractIosProtoRequest(string BinaryBase64);

    public sealed record DiagnoseRequest(string Base64, string? RootType);
}

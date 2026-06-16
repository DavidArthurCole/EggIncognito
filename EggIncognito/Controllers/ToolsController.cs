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
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        // APK + IPA are both zips (PK\x03\x04); the archive extractor locates the native binary entry
        // inside (Android .so / iOS Payload/*.app exec) and carves it. A non-zip is a raw binary.
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var r = isZip
            ? Services.ProtoExtract.ArchiveProtoExtractor.Extract(bytes)
            : Services.ProtoExtract.DescriptorProtoCarver.Extract(bytes);
        return ExtractResultJson(r);
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

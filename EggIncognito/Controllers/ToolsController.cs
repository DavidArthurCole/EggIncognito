using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Read-only tooling, hosted-safe (no writes): Postman export, blob decode, endpoint status.
[ApiController]
[Route("api/tools")]
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

// EggIncognito/Controllers/InspectorApiController.cs
//
// Backend for the Transport Inspector SPA. Hand-written (not source-generated). Its
// routes live under /api/inspector so they never collide with the generated mock
// endpoint controllers (which are routed by their Egg, Inc. API path).

using System.Text.Json;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/inspector")]
public sealed class InspectorApiController(
    IEndpointCatalog catalog,
    IProtoReflection reflection,
    ITransportPipeline pipeline,
    IHttpClientFactory httpFactory) : ControllerBase
{
    // Hosts the UI is allowed to POST to. Prevents the /send route from being an open proxy.
    private static readonly string[] AllowedSendHostSuffixes =
    [
        "auxbrain.com",
        "auxbrainhome.appspot.com",
    ];

    [HttpGet("endpoints")]
    public IActionResult Endpoints()
    {
        var list = catalog.All()
            .Where(e => e.RequestType is not null)
            .Select(e => new
            {
                e.Path,
                @namespace = e.Path.Split('/')[0],
                e.RequestType,
                e.ResponseType,
                e.PathParam,
                e.Wrap,
            });
        return Ok(list);
    }

    [HttpGet("schema/{typeName}")]
    public IActionResult Schema(string typeName)
    {
        var schema = reflection.Schema(typeName);
        return schema is null
            ? NotFound(new { error = $"unknown message type '{typeName}'" })
            : Ok(schema);
    }

    [HttpGet("env-defaults")]
    public IActionResult EnvDefaults() => Ok(DefaultRInfo);

    public sealed record BuildRequest(
        string Path,
        string RequestType,
        bool Wrap,
        JsonElement? Fields,
        JsonElement? Env);

    [HttpPost("build")]
    public IActionResult Build([FromBody] BuildRequest body)
    {
        var parser = reflection.FindParser(body.RequestType);
        var descriptor = reflection.FindMessage(body.RequestType);
        if (parser is null || descriptor is null)
            return BadRequest(new { error = $"unknown request type '{body.RequestType}'" });

        IMessage message;
        try
        {
            var fieldsJson = body.Fields?.GetRawText() ?? "{}";
            var merged = MergeEnv(fieldsJson, body.Env, descriptor);
            message = parser.ParseJson(merged);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"invalid request JSON: {ex.Message}" });
        }

        var inner = message.ToByteArray();
        var result = pipeline.Build(inner, body.Wrap);

        return Ok(new
        {
            result.Stages,
            result.FinalBase64,
            result.FinalFormBody,
            canSign = pipeline.CanSign,
        });
    }

    public sealed record SendRequest(string Url, string FormBody, string? ResponseType);

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendRequest body)
    {
        if (!IsAllowedUrl(body.Url, out var uri))
            return BadRequest(new { error = "target URL host is not allowed" });

        var client = httpFactory.CreateClient("inspector");
        var content = new StringContent(body.FormBody,
            System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsync(uri, content);
        }
        catch (Exception ex)
        {
            return Ok(new { error = $"send failed: {ex.Message}" });
        }

        var raw = (await resp.Content.ReadAsStringAsync()).Trim();
        var parser = body.ResponseType is not null
            ? reflection.FindParser(body.ResponseType)
            : null;

        var decode = pipeline.Decode(raw, parser);
        return Ok(new
        {
            status = (int)resp.StatusCode,
            rawBase64 = raw,
            decode.Stages,
            json = decode.Json,
            error = decode.Error,
        });
    }

    // Merge the env panel's BasicRequestInfo overrides onto the message's `rinfo` field,
    // but only for keys the user did not already set explicitly in the JSON tree.
    private static string MergeEnv(string fieldsJson, JsonElement? env,
        Google.Protobuf.Reflection.MessageDescriptor descriptor)
    {
        var rinfoField = descriptor.Fields.InFieldNumberOrder()
            .FirstOrDefault(f => f.Name == "rinfo");
        if (env is null || rinfoField is null) return fieldsJson;

        using var fieldsDoc = JsonDocument.Parse(fieldsJson);
        var root = fieldsDoc.RootElement;

        var outObj = new Dictionary<string, JsonElement>();
        foreach (var prop in root.EnumerateObject())
            outObj[prop.Name] = prop.Value.Clone();

        // Existing rinfo (if any) wins per-key over env defaults.
        var rinfoKey = rinfoField.JsonName;
        var envObj = new Dictionary<string, JsonElement>();
        foreach (var prop in env.Value.EnumerateObject())
            envObj[prop.Name] = prop.Value.Clone();

        if (root.TryGetProperty(rinfoKey, out var existingRinfo) && existingRinfo.ValueKind == JsonValueKind.Object)
            foreach (var prop in existingRinfo.EnumerateObject())
                envObj[prop.Name] = prop.Value.Clone();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var kv in outObj)
            {
                if (kv.Key == rinfoKey) continue; // rewritten below from merged env
                writer.WritePropertyName(kv.Key);
                kv.Value.WriteTo(writer);
            }
            writer.WritePropertyName(rinfoKey);
            writer.WriteStartObject();
            foreach (var kv in envObj)
            {
                writer.WritePropertyName(kv.Key);
                kv.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool IsAllowedUrl(string url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        var host = parsed.Host;
        var ok = host is "localhost" or "127.0.0.1"
                 || AllowedSendHostSuffixes.Any(s =>
                        host.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                        host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));
        if (!ok) return false;
        uri = parsed;
        return true;
    }

    // BasicRequestInfo defaults - mirrors the Seeder's BuildRInfo constants.
    private static readonly object DefaultRInfo = new
    {
        clientVersion = 72,
        version = "1.35.7",
        build = "111343",
        platform = "DROID",
        country = "US",
        language = "en",
        debug = false,
    };
}

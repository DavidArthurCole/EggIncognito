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
    IRouteCatalog catalog,
    IProtoReflection reflection,
    ITransportPipeline pipeline,
    IHttpClientFactory httpFactory,
    ILogger<InspectorApiController> logger) : ControllerBase
{
    [HttpGet("endpoints")]
    public IActionResult Endpoints()
    {
        var list = catalog.All()
            .Select(e => new
            {
                e.Path,
                @namespace = e.Path.Split('/')[0],
                e.Request,
                e.Response,
                e.RequestWrapped,
                e.ResponseWrapped,
                e.PathParam,
                e.PathParamOnly,
                e.RawResponse,
            });
        return Ok(list);
    }

    [HttpGet("schema/{typeName}")]
    public IActionResult Schema(string typeName)
    {
        var schema = reflection.Schema(typeName);
        if (schema is null)
            throw new ApiException(
                $"unknown message type '{typeName}'",
                "Type not found in the compiled proto. Check spelling, or run scripts/Sync-Proto.ps1 if it is a new upstream type.",
                StatusCodes.Status404NotFound);
        return Ok(schema);
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
            throw new ApiException(
                $"unknown request type '{body.RequestType}'",
                "Type not found in the compiled proto. Check the endpoint's request type in routes.yaml.",
                StatusCodes.Status400BadRequest);

        IMessage message;
        try
        {
            var fieldsJson = body.Fields?.GetRawText() ?? "{}";
            var merged = MergeEnv(fieldsJson, body.Env, descriptor);
            message = parser.ParseJson(merged);
        }
        catch (Exception ex)
        {
            throw new ApiException(
                $"invalid request JSON: {ex.Message}",
                $"Field values do not match {body.RequestType}. Check field types in the schema panel.",
                StatusCodes.Status400BadRequest);
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
        var uri = ResolveAllowedUrl(body.Url);

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
            logger.LogWarning(ex, "send {Host}{Path} -> FAILED", uri.Host, uri.AbsolutePath);
            // In-band error: the SPA renders this inline rather than as an HTTP failure.
            return Ok(new ApiError(
                $"send failed: {ex.Message}",
                "Check the target host is reachable and the URL is correct.",
                StatusCodes.Status502BadGateway));
        }

        logger.LogInformation("send {Host}{Path} -> HTTP {Status} (signing {Signing})",
            uri.Host, uri.AbsolutePath, (int)resp.StatusCode, pipeline.CanSign ? "ready" : "off");

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
            resolution = decode.Error is null ? null
                : "No known response type for this endpoint. Add a `response:` type in routes.yaml so the body can be decoded.",
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

    // Validates the send target against the host allowlist and returns the parsed Uri,
    // or throws ApiException with a resolution. Prevents /send from being an open proxy.
    private static Uri ResolveAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            throw new ApiException(
                $"invalid target URL '{url}'",
                "URL must be an absolute http(s) URL.",
                StatusCodes.Status400BadRequest);

        if (IsAllowedHost(parsed.Host)) return parsed;

        throw new ApiException(
            $"target URL host '{parsed.Host}' is not allowed",
            "Allowed hosts: localhost, 127.0.0.1, *.auxbrain.com, auxbrainhome.appspot.com, and its <service>-dot-auxbrainhome.appspot.com subdomains.",
            StatusCodes.Status400BadRequest);
    }

    // The /send target allowlist = auxbrain hosts (shared rule) plus localhost for the mock.
    internal static bool IsAllowedHost(string host) =>
        host is "localhost" or "127.0.0.1" || AuxbrainHosts.IsAuxbrain(host);

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

// Backend for the Transport Inspector SPA. Hand-written, not source-generated. Routes live under
// /api/inspector so they never collide with the generated mock endpoint controllers, which are routed
// by their Egg, Inc. API path.

using System.Text.Json;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/inspector")]
#pragma warning disable S107 // Inspector backend genuinely depends on each of these injected services.
public sealed class InspectorApiController(
    IRouteCatalog catalog,
    IProtoReflection reflection,
    ITransportPipeline pipeline,
    IHttpClientFactory httpFactory,
    IAppMode appMode,
    ICurrentUser currentUser,
    ISealedProxy sealedProxy,
    ILogger<InspectorApiController> logger) : ControllerBase
#pragma warning restore S107
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
        // Short browser cache: the catalog is yaml-driven plus rare DB routes. The Reload button
        // bypasses cache for an explicit refresh.
        Response.Headers.CacheControl = "private, max-age=20";
        return Ok(list);
    }

    // Proto is frozen per process; let the browser cache it.
    [HttpGet("messages")]
    public IActionResult Messages()
    {
        Response.Headers.CacheControl = "private, max-age=300";
        return Ok(reflection.AllMessageTypeNames());
    }

    [HttpGet("schema/{typeName}")]
    public IActionResult Schema(string typeName)
    {
        var schema = reflection.Schema(typeName);
        if (schema is null)
            throw new ApiException(
                $"unknown message type '{typeName}'",
                "Type not found in the compiled proto. Check spelling; ei.proto is a frozen upstream snapshot, so a genuinely new type needs ei.proto edited and the solution rebuilt.",
                StatusCodes.Status404NotFound);
        return Ok(schema);
    }

    public sealed record BuildRequest(
        string Path,
        string RequestType,
        bool Wrap,
        JsonElement? Fields,
        JsonElement? Env,
        string? Salt);

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
        // The salt is client-owned: it rides in this request and is used only for this build.
        var result = pipeline.Build(inner, body.Wrap, body.Salt);

        return Ok(new
        {
            result.Stages,
            result.FinalBase64,
            result.FinalFormBody,
            canSign = !string.IsNullOrEmpty(body.Salt),
        });
    }

    public sealed record SendRequest(string Url, string FormBody, string? ResponseType, bool Sealed = false, bool? ResponseWrapped = null);

    [HttpPost("send")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> Send([FromBody] SendRequest body)
    {
        // The /send egress makes an outbound auxbrain call from this server; when hosted, only for an
        // authenticated user.
        if (appMode.Mode == AppMode.Hosted && !currentUser.IsAuthenticated)
            throw new ApiException(
                "log in to use Live API from the hosted site",
                "Sign in with Discord, then retry. Local runs are never gated.",
                StatusCodes.Status403Forbidden);

        // Sealed API proxy (supporter perk): fail-closed, a non-supporter or unconfigured upstream
        // asking for sealed mode is rejected rather than silently sent direct.
        var useSealed = body.Sealed;
        if (useSealed && !await sealedProxy.CanUseAsync(currentUser, HttpContext.RequestAborted))
            throw new ApiException(
                "the sealed API proxy is a supporter perk",
                "Become a supporter and enable it, or send without sealed mode.",
                StatusCodes.Status403Forbidden);

        var uri = ResolveAllowedUrl(body.Url, Request.Host.Host);

        var client = useSealed ? sealedProxy.CreateEgressClient() : httpFactory.CreateClient("inspector");
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
            // In-band error so the SPA renders this inline rather than as an HTTP failure.
            return Ok(new ApiError(
                $"send failed: {ex.Message}",
                "Check the target host is reachable and the URL is correct.",
                StatusCodes.Status502BadGateway));
        }

        logger.LogInformation("send {Host}{Path} -> HTTP {Status}",
            uri.Host, uri.AbsolutePath, (int)resp.StatusCode);

        var raw = (await resp.Content.ReadAsStringAsync()).Trim();
        var parser = body.ResponseType is not null
            ? reflection.FindParser(body.ResponseType)
            : null;

        var decode = pipeline.Decode(raw, parser, body.ResponseWrapped);
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

    public sealed record DecodeResponseRequest(string RawBase64, string? ResponseType, bool? ResponseWrapped = null);

    [HttpPost("decode-response")]
    public IActionResult DecodeResponse([FromBody] DecodeResponseRequest body)
    {
        // Pure decode of a response the browser already has in custom-proxy mode. No network, no salt,
        // no egress, just proto reflection. Ungated; renders the same decoded view Mock/Live get.
        var parser = body.ResponseType is not null ? reflection.FindParser(body.ResponseType) : null;
        var decode = pipeline.Decode(body.RawBase64, parser, body.ResponseWrapped);
        return Ok(new { decode.Stages, json = decode.Json, error = decode.Error });
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
                if (kv.Key == rinfoKey) continue;
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

    // Validates the send target against the host allowlist and returns the parsed Uri, or throws
    // ApiException with a resolution. selfHost is always allowed so "Mock (this instance)" works on a
    // public deploy; every other target falls back to the auxbrain + localhost rule.
    private static Uri ResolveAllowedUrl(string url, string selfHost)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            throw new ApiException(
                $"invalid target URL '{url}'",
                "URL must be an absolute http(s) URL.",
                StatusCodes.Status400BadRequest);

        if (IsAllowedHost(parsed.Host, selfHost)) return parsed;

        throw new ApiException(
            $"target URL host '{parsed.Host}' is not allowed",
            "Allowed hosts: this instance, localhost, 127.0.0.1, *.auxbrain.com, auxbrainhome.appspot.com, and its <service>-dot-auxbrainhome.appspot.com subdomains.",
            StatusCodes.Status400BadRequest);
    }

    // The /send target allowlist = auxbrain hosts + localhost + this instance's own host. selfHost is
    // compared case-insensitively; empty selfHost falls through to the static rules.
    internal static bool IsAllowedHost(string host, string? selfHost = null) =>
        host is "localhost" or "127.0.0.1"
        || (!string.IsNullOrEmpty(selfHost) && string.Equals(host, selfHost, StringComparison.OrdinalIgnoreCase))
        || AuxbrainHosts.IsAuxbrain(host);

}

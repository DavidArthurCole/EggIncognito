using System.Collections.Generic;
using System.Linq;

namespace EggIncognito.Generator;

public sealed class RouteModel
{
    public string Path { get; set; } = "";

    /// <summary>Inner request proto type, or null when the route has no request body
    /// (path-param-only) or its inner type is not yet known.</summary>
    public string? Request { get; set; }

    /// <summary>Inner response proto type, or null when not yet known.</summary>
    public string? Response { get; set; }

    /// <summary>Request is signed and wrapped in an AuthenticatedMessage on the wire.</summary>
    public bool RequestWrapped { get; set; }

    /// <summary>Response is wrapped in an AuthenticatedMessage and must be unwrapped to Response.</summary>
    public bool ResponseWrapped { get; set; }

    /// <summary>When set, the route returns this literal string instead of encoded protobuf.</summary>
    public string? RawResponse { get; set; }

    public bool PathParam { get; set; }

    /// <summary>No request body; identity (EID) is carried in the URL path parameter.</summary>
    public bool PathParamOnly { get; set; }

    /// <summary>The proto type the MOCK serializes for its response. Falls back to
    /// AuthenticatedMessage when the inner type is not yet known, preserving the
    /// pre-migration behavior (an empty envelope) until the type is captured.</summary>
    public string MockResponseType => Response ?? "AuthenticatedMessage";
}

public static class RouteParser
{
    // Raw, pre-normalization line values collected per route block.
    private sealed class Block
    {
        public string? Path;
        public string? Request, Response, RawResponse, LegacyReq, LegacyRes;
        public bool? RequestWrapped, ResponseWrapped;
        public bool PathParam, PathParamOnly;
    }

    public static List<RouteModel> Parse(string yaml)
    {
        var results = new List<RouteModel>();
        Block? b = null;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var trimmed = rawLine.TrimStart().TrimEnd();

            if (trimmed.StartsWith("- path:"))
            {
                if (b != null) results.Add(Emit(b));
                b = new Block { Path = trimmed.Substring("- path:".Length).Trim().TrimEnd('/') };
            }
            else if (b != null)
            {
                ApplyLine(b, trimmed);
            }
        }
        if (b != null) results.Add(Emit(b));
        return results;
    }

    private static void ApplyLine(Block b, string trimmed)
    {
        if (trimmed.StartsWith("requestType:")) b.LegacyReq = After(trimmed, "requestType:");
        else if (trimmed.StartsWith("responseType:")) b.LegacyRes = After(trimmed, "responseType:");
        else if (trimmed.StartsWith("request:")) b.Request = NullIfEmpty(After(trimmed, "request:"));
        else if (trimmed.StartsWith("response:")) b.Response = NullIfEmpty(After(trimmed, "response:"));
        else if (trimmed.StartsWith("requestWrapped:")) b.RequestWrapped = After(trimmed, "requestWrapped:") == "true";
        else if (trimmed.StartsWith("responseWrapped:")) b.ResponseWrapped = After(trimmed, "responseWrapped:") == "true";
        else if (trimmed.StartsWith("rawResponse:")) b.RawResponse = After(trimmed, "rawResponse:").Trim('"');
        else if (trimmed.StartsWith("pathParamOnly:")) b.PathParamOnly = After(trimmed, "pathParamOnly:") == "true";
        else if (trimmed.StartsWith("pathParam:")) b.PathParam = After(trimmed, "pathParam:") == "true";
    }

    private static string After(string line, string key)
    {
        var v = line.Substring(key.Length);
        var hash = v.IndexOf('#'); // strip inline comments
        if (hash >= 0) v = v.Substring(0, hash);
        return v.Trim();
    }
    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    // Normalization shared (by convention) with RouteCatalog and RouteLoader.
    // New `request`/`response` keys win. Legacy `requestType`/`responseType` are read as
    // aliases; the literal "AuthenticatedMessage" in a legacy field means "signed/wrapped
    // envelope, inner type not yet known" -> null inner type + the matching wrapped flag.
    private static RouteModel Emit(Block b)
    {
        var (reqType, reqWrapDefault) = Normalize(b.Request, b.LegacyReq);
        var (resType, resWrapDefault) = Normalize(b.Response, b.LegacyRes);
        return new RouteModel
        {
            Path = b.Path!,
            Request = b.PathParamOnly ? null : reqType,
            Response = resType,
            RequestWrapped = b.RequestWrapped ?? reqWrapDefault,
            ResponseWrapped = b.ResponseWrapped ?? resWrapDefault,
            RawResponse = b.RawResponse,
            PathParam = b.PathParam,
            PathParamOnly = b.PathParamOnly,
        };
    }

    // Returns (innerType, wrappedDefault). "AuthenticatedMessage" -> (null, true).
    private static (string? type, bool wrapped) Normalize(string? newKey, string? legacy)
    {
        var v = newKey ?? legacy;
        if (v == null) return (null, false);
        if (v == "AuthenticatedMessage") return (null, true);
        return (v, false);
    }

    public static string ToClassName(string path) =>
        string.Concat(
            path.Split(new[] { '/', '_' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(s => char.ToUpper(s[0]) + s.Substring(1))
        ) + "Controller";
}

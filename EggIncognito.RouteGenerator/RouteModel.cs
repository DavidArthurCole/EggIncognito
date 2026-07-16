using System;
using System.Collections.Generic;
using System.Linq;

namespace EggIncognito.RouteGenerator;

public sealed class RouteModel : IEquatable<RouteModel>
{
    public string Path { get; set; } = "";

   
   
    public string? Request { get; set; }

   
    public string? Response { get; set; }

   
    public bool RequestWrapped { get; set; }

   
    public bool ResponseWrapped { get; set; }

   
    public string? RawResponse { get; set; }

    public bool PathParam { get; set; }

    public bool PathParamOnly { get; set; }

   
   
    public string MockResponseType => Response ?? "AuthenticatedMessage";

   
    public bool Equals(RouteModel? other) =>
        other is not null
        && Path == other.Path
        && Request == other.Request
        && Response == other.Response
        && RequestWrapped == other.RequestWrapped
        && ResponseWrapped == other.ResponseWrapped
        && RawResponse == other.RawResponse
        && PathParam == other.PathParam
        && PathParamOnly == other.PathParamOnly;

    public override bool Equals(object? obj) => Equals(obj as RouteModel);

    public override int GetHashCode()
    {
        unchecked
        {
            var h = Path.GetHashCode();
            h = h * 31 + (Request?.GetHashCode() ?? 0);
            h = h * 31 + (Response?.GetHashCode() ?? 0);
            h = h * 31 + (RawResponse?.GetHashCode() ?? 0);
            h = h * 31 + ((RequestWrapped ? 1 : 0) | (ResponseWrapped ? 2 : 0)
                | (PathParam ? 4 : 0) | (PathParamOnly ? 8 : 0));
            return h;
        }
    }
}
public sealed class RouteListComparer : IEqualityComparer<List<RouteModel>>
{
    public static readonly RouteListComparer Instance = new RouteListComparer();
    private RouteListComparer() { }

    public bool Equals(List<RouteModel>? x, List<RouteModel>? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Count != y.Count) return false;
        for (var i = 0; i < x.Count; i++)
            if (!x[i].Equals(y[i])) return false;
        return true;
    }

    public int GetHashCode(List<RouteModel> obj)
    {
        unchecked
        {
            var h = obj.Count;
            foreach (var r in obj) h = h * 31 + r.GetHashCode();
            return h;
        }
    }
}

public static class RouteParser
{
    private sealed class Block
    {
        public string? Path;
        public string? Request, Response, RawResponse, LegacyReq, LegacyRes;
        public bool? RequestWrapped, ResponseWrapped;
        public bool PathParam, PathParamOnly, HasRequest, HasResponse;
    }

    public static List<RouteModel> Parse(string yaml)
    {
        var results = new List<RouteModel>();
        Block? b = null;
        var inRoutes = false;

        void Flush()
        {
            if (b?.Path is not null) results.Add(Emit(b));
            b = null;
        }

        foreach (var rawLine in yaml.Split('\n'))
        {
            var trimmed = rawLine.TrimStart().TrimEnd();

           
            if (IsTopLevelKey(rawLine, trimmed))
            {
                Flush();
                inRoutes = trimmed == "routes:";
                continue;
            }
            if (!inRoutes) continue;

            if (trimmed.StartsWith("- path:"))
            {
                Flush();
                var path = trimmed.Substring("- path:".Length).Trim().TrimEnd('/');
                b = new Block { Path = path.Length == 0 ? null : path };
            }
            else if (b != null)
            {
                ApplyLine(b, trimmed);
            }
        }
        Flush();
        return results;
    }

   
    private static bool IsTopLevelKey(string rawLine, string trimmed)
    {
        if (rawLine.Length == 0 || char.IsWhiteSpace(rawLine[0])) return false;
        if (trimmed.Length < 2 || trimmed[trimmed.Length - 1] != ':') return false;
        for (var i = 0; i < trimmed.Length - 1; i++)
        {
            var c = trimmed[i];
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static void ApplyLine(Block b, string trimmed)
    {
        if (trimmed.StartsWith("requestType:")) b.LegacyReq = After(trimmed, "requestType:");
        else if (trimmed.StartsWith("responseType:")) b.LegacyRes = After(trimmed, "responseType:");
        else if (trimmed.StartsWith("request:")) { b.Request = NullIfEmpty(After(trimmed, "request:")); b.HasRequest = true; }
        else if (trimmed.StartsWith("response:")) { b.Response = NullIfEmpty(After(trimmed, "response:")); b.HasResponse = true; }
        else if (trimmed.StartsWith("requestWrapped:")) b.RequestWrapped = After(trimmed, "requestWrapped:") == "true";
        else if (trimmed.StartsWith("responseWrapped:")) b.ResponseWrapped = After(trimmed, "responseWrapped:") == "true";
        else if (trimmed.StartsWith("rawResponse:")) b.RawResponse = After(trimmed, "rawResponse:").Trim('"');
        else if (trimmed.StartsWith("pathParamOnly:")) b.PathParamOnly = After(trimmed, "pathParamOnly:") == "true";
        else if (trimmed.StartsWith("pathParam:")) b.PathParam = After(trimmed, "pathParam:") == "true";
    }

    private static string After(string line, string key)
    {
        var v = line.Substring(key.Length);
        var hash = v.IndexOf('#');
        if (hash >= 0) v = v.Substring(0, hash);
        return v.Trim();
    }
    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

   
    private static RouteModel Emit(Block b)
    {
        var (reqType, reqWrapDefault) = Normalize(b.HasRequest ? b.Request : b.LegacyReq);
        var (resType, resWrapDefault) = Normalize(b.HasResponse ? b.Response : b.LegacyRes);
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

    private static (string? type, bool wrapped) Normalize(string? v)
    {
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

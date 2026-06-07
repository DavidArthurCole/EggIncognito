// EggIncognito/Services/RouteCatalog.cs
//
// Runtime view of routes.yaml for the Inspector UI. The source generator parses
// the same file at compile time to emit controllers; this is the runtime equivalent
// so the UI knows each route's request/response type and the transport framing.
//
// Normalization rules are kept identical (by convention) with the Generator's
// RouteParser and the CodeGen RouteLoader: new `request`/`response` keys win;
// legacy `requestType`/`responseType` are aliases; the literal "AuthenticatedMessage"
// in a legacy field means "wrapped, inner type not yet known" -> null inner + wrapped.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services;

public sealed record RouteInfo(
    string Path,
    string? Request,
    string? Response,
    bool RequestWrapped,
    bool ResponseWrapped,
    string? RawResponse,
    bool PathParam,
    bool PathParamOnly);

public interface IRouteCatalog
{
    IReadOnlyList<RouteInfo> All();
    RouteInfo? Get(string path);
}

public sealed class RouteCatalog : IRouteCatalog
{
    private readonly IReadOnlyList<RouteInfo> _routes;
    private readonly Dictionary<string, RouteInfo> _byPath;

    public RouteCatalog(IConfiguration config)
        : this(ResolveYamlPath(config)) { }

    public RouteCatalog(string yamlPath)
    {
        var list = File.Exists(yamlPath) ? Parse(File.ReadAllText(yamlPath)) : [];
        _routes = list;
        _byPath = list.ToDictionary(e => e.Path, StringComparer.Ordinal);
    }

    public IReadOnlyList<RouteInfo> All() => _routes;

    public RouteInfo? Get(string path) =>
        _byPath.TryGetValue(path, out var e) ? e : null;

    private static string ResolveYamlPath(IConfiguration config)
    {
        var configured = config["RoutesYamlPath"];
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

        // Search up from the app base dir for RouteMap/routes.yaml. Works whether
        // running from bin/ (dev) or the published app dir.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "RouteMap", "routes.yaml");
            if (File.Exists(candidate)) return candidate;
            var nested = Path.Combine(dir.FullName, "EggIncognito", "RouteMap", "routes.yaml");
            if (File.Exists(nested)) return nested;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "RouteMap", "routes.yaml");
    }

    // Raw, pre-normalization values collected per route block.
    private sealed class Block
    {
        public string? Path, Request, Response, RawResponse, LegacyReq, LegacyRes;
        public bool? RequestWrapped, ResponseWrapped;
        public bool PathParam, PathParamOnly;
    }

    // Minimal line-based parser. Only reads the `routes:` section (stops at the next
    // top-level key like `excluded:` / `endpoint_status:`).
    internal static List<RouteInfo> Parse(string yaml)
    {
        var result = new List<RouteInfo>();
        Block? b = null;
        bool inRoutes = false;

        void Flush()
        {
            if (b?.Path is not null) result.Add(Emit(b));
            b = null;
        }

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Top-level key (no leading whitespace, ends with ':').
            var topKey = Regex.Match(line, @"^(\w[\w_]*):\s*$");
            if (topKey.Success)
            {
                Flush();
                inRoutes = topKey.Groups[1].Value == "routes";
                continue;
            }
            if (!inRoutes) continue;

            var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+?)\s*$");
            if (pathMatch.Success) { Flush(); b = new Block { Path = pathMatch.Groups[1].Value.Trim().TrimEnd('/') }; continue; }
            if (b is null) continue;

            ApplyLine(b, line);
        }
        Flush();
        return result;
    }

    private static void ApplyLine(Block b, string line)
    {
        string? V(string key)
        {
            // Value is everything up to an optional inline `#` comment, trimmed.
            var m = Regex.Match(line, @"^\s+" + Regex.Escape(key) + @":\s*([^#]*?)\s*(?:#.*)?$");
            return m.Success ? m.Groups[1].Value : null;
        }

        string? v;
        if ((v = V("requestType")) is not null) b.LegacyReq = v;
        else if ((v = V("responseType")) is not null) b.LegacyRes = v;
        else if ((v = V("request")) is not null) b.Request = NullIfEmpty(v);
        else if ((v = V("response")) is not null) b.Response = NullIfEmpty(v);
        else if ((v = V("requestWrapped")) is not null) b.RequestWrapped = v == "true";
        else if ((v = V("responseWrapped")) is not null) b.ResponseWrapped = v == "true";
        else if ((v = V("rawResponse")) is not null) b.RawResponse = v.Trim('"');
        else if ((v = V("pathParamOnly")) is not null) b.PathParamOnly = v == "true";
        else if ((v = V("pathParam")) is not null) b.PathParam = v == "true";
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static RouteInfo Emit(Block b)
    {
        var (reqType, reqWrapDefault) = Normalize(b.Request, b.LegacyReq);
        var (resType, resWrapDefault) = Normalize(b.Response, b.LegacyRes);
        return new RouteInfo(
            Path: b.Path!,
            Request: b.PathParamOnly ? null : reqType,
            Response: resType,
            RequestWrapped: b.RequestWrapped ?? reqWrapDefault,
            ResponseWrapped: b.ResponseWrapped ?? resWrapDefault,
            RawResponse: b.RawResponse,
            PathParam: b.PathParam,
            PathParamOnly: b.PathParamOnly);
    }

    // Returns (innerType, wrappedDefault). "AuthenticatedMessage" -> (null, true).
    private static (string? type, bool wrapped) Normalize(string? newKey, string? legacy)
    {
        var v = newKey ?? legacy;
        if (v is null) return (null, false);
        if (v == "AuthenticatedMessage") return (null, true);
        return (v, false);
    }
}

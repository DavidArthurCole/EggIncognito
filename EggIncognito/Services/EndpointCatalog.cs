// EggIncognito/Services/EndpointCatalog.cs
//
// Runtime view of endpoints.yaml for the Inspector UI. The source generator parses
// the same file at compile time to emit controllers; this is the runtime equivalent
// so the UI knows each endpoint's request/response type and whether it wraps in an
// AuthenticatedMessage.

using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public sealed record EndpointInfo(
    string Path,
    string? RequestType,
    string? ResponseType,
    string? RawResponse,
    bool PathParam,
    bool Wrap);

public interface IEndpointCatalog
{
    IReadOnlyList<EndpointInfo> All();
    EndpointInfo? Get(string path);
}

public sealed class EndpointCatalog : IEndpointCatalog
{
    private readonly IReadOnlyList<EndpointInfo> _endpoints;
    private readonly Dictionary<string, EndpointInfo> _byPath;

    // Endpoints that wrap the request in an AuthenticatedMessage before posting.
    // Mirrors the Seeder's `Wrap` flags. Everything else posts the raw request bytes.
    private static readonly HashSet<string> WrapPaths = new(StringComparer.Ordinal)
    {
        "ei/first_contact_secure",
        "ei/clean_accounts",
        "ei/clear_all_user_data",
        "ei_afx/consume_artifact",
    };

    public EndpointCatalog(IConfiguration config)
        : this(ResolveYamlPath(config)) { }

    internal EndpointCatalog(string yamlPath)
    {
        var list = File.Exists(yamlPath) ? Parse(File.ReadAllText(yamlPath)) : [];
        _endpoints = list;
        _byPath = list.ToDictionary(e => e.Path, StringComparer.Ordinal);
    }

    public IReadOnlyList<EndpointInfo> All() => _endpoints;

    public EndpointInfo? Get(string path) =>
        _byPath.TryGetValue(path, out var e) ? e : null;

    private static string ResolveYamlPath(IConfiguration config)
    {
        var configured = config["EndpointsYamlPath"];
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

        // Search up from the app base dir for EndpointMap/endpoints.yaml. Works whether
        // running from bin/ (dev) or the published app dir.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "EndpointMap", "endpoints.yaml");
            if (File.Exists(candidate)) return candidate;
            var nested = Path.Combine(dir.FullName, "EggIncognito", "EndpointMap", "endpoints.yaml");
            if (File.Exists(nested)) return nested;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "EndpointMap", "endpoints.yaml");
    }

    // Minimal line-based parser. Only reads the `endpoints:` section (stops at the next
    // top-level key like `excluded:` / `fixture_status:`).
    internal static List<EndpointInfo> Parse(string yaml)
    {
        var result = new List<EndpointInfo>();
        string? path = null, req = null, res = null, raw = null;
        bool pathParam = false;
        bool inEndpoints = false;

        void Flush()
        {
            if (path is not null)
                result.Add(new EndpointInfo(path, req, res, raw, pathParam, WrapPaths.Contains(path)));
            path = req = res = raw = null;
            pathParam = false;
        }

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Top-level key (no leading whitespace, ends with ':').
            var topKey = Regex.Match(line, @"^(\w[\w_]*):\s*$");
            if (topKey.Success)
            {
                Flush();
                inEndpoints = topKey.Groups[1].Value == "endpoints";
                continue;
            }
            if (!inEndpoints) continue;

            var pathMatch = Regex.Match(line, @"^\s+-\s+path:\s+(.+?)\s*$");
            if (pathMatch.Success) { Flush(); path = pathMatch.Groups[1].Value.Trim(); continue; }
            if (path is null) continue;

            var m = Regex.Match(line, @"^\s+requestType:\s+(.+?)\s*$");
            if (m.Success) { req = m.Groups[1].Value.Trim(); continue; }
            m = Regex.Match(line, @"^\s+responseType:\s+(.+?)\s*$");
            if (m.Success) { res = m.Groups[1].Value.Trim(); continue; }
            m = Regex.Match(line, @"^\s+rawResponse:\s+(.+?)\s*$");
            if (m.Success) { raw = m.Groups[1].Value.Trim().Trim('"'); continue; }
            m = Regex.Match(line, @"^\s+pathParam:\s+(.+?)\s*$");
            if (m.Success) { pathParam = m.Groups[1].Value.Trim() == "true"; continue; }
        }
        Flush();
        return result;
    }
}

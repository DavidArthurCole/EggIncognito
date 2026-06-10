using System.Text;

namespace EggIncognito.Services;

// The file-backed endpoint source: loads every default/*.json + eids/<eid>/*.json under the root at
// construction, keyed by relative slug (no .json). Lookup applies eid-beats-default precedence and
// the path-parameter parent walk (e.g. ei_ctx/get_eval/<id> falls back to ei_ctx/get_eval). This is
// the loader formerly inlined in EndpointStore; behavior is preserved exactly.
public sealed class FileEndpointSource : IEndpointSource
{
    private readonly Dictionary<string, byte[]> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    public int Priority => 0;

    public FileEndpointSource(string endpointsPath)
    {
        if (!Directory.Exists(endpointsPath)) return;
        foreach (var file in Directory.EnumerateFiles(endpointsPath, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(endpointsPath, file).Replace('\\', '/').Replace(".json", "");
            try { _endpoints[relative] = File.ReadAllBytes(file); }
            catch { /* skip unreadable file; mirrors prior best-effort load */ }
        }
    }

    public byte[]? Lookup(string path, string? eid)
    {
        var cleanPath = path.TrimEnd('/');
        while (true)
        {
            if (eid is not null && _endpoints.TryGetValue($"eids/{eid}/{cleanPath}", out var eidBytes))
                return eidBytes;
            if (_endpoints.TryGetValue($"default/{cleanPath}", out var defaultBytes))
                return defaultBytes;

            var lastSlash = cleanPath.LastIndexOf('/');
            var firstSlash = cleanPath.IndexOf('/');
            if (lastSlash <= firstSlash) break;
            cleanPath = cleanPath[..lastSlash];
        }
        return null;
    }
}

using System.Text;

namespace EggIncognito.Services;

// The file-backed endpoint source: loads every default/*.json + eids/<eid>/*.json under the root at
// construction, keyed by relative slug without the .json. Lookup applies eid-beats-default precedence
// and the path-parameter parent walk, where ei_ctx/get_eval/<id> falls back to ei_ctx/get_eval.
public sealed class FileEndpointSource : IEndpointSource
{
    private readonly Dictionary<string, byte[]> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    public int Priority => 0;
    public int Count => _endpoints.Count;

    public FileEndpointSource(string endpointsPath)
    {
        if (!Directory.Exists(endpointsPath)) return;
        // IgnoreInaccessible skips subdirectories/files the process cannot read (e.g. root-owned
        // /tmp/systemd-private-* when the root is a shared temp dir) instead of throwing mid-enumeration.
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(endpointsPath, "*.json", opts))
        {
            var relative = Path.GetRelativePath(endpointsPath, file).Replace('\\', '/').Replace(".json", "");
            try { _endpoints[relative] = File.ReadAllBytes(file); }
            catch { }
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

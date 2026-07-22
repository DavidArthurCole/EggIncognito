namespace EggIncognito.Services;


public sealed class FileEndpointSource : IEndpointSource {
    private readonly Dictionary<string, byte[]> _endpoints = [with(StringComparer.OrdinalIgnoreCase)];
    public int Priority => 0;
    public int Count => _endpoints.Count;

    public FileEndpointSource(string endpointsPath) {
        if (!Directory.Exists(endpointsPath)) return;


        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(endpointsPath, "*.json", opts)) {
            var relative = Path.GetRelativePath(endpointsPath, file).Replace('\\', '/').Replace(".json", "");
            try { _endpoints[relative] = File.ReadAllBytes(file); } catch { }
        }
    }

    public byte[]? Lookup(string path, string? eid) {
        var cleanPath = path.TrimEnd('/');
        while (true) {
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

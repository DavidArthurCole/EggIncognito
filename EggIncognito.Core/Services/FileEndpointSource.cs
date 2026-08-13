using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;

public sealed class FileEndpointSource : IEndpointSource {
    private readonly Dictionary<string, byte[]> _endpoints = [with(StringComparer.OrdinalIgnoreCase)];

    public FileEndpointSource(string endpointsPath, ILogger<FileEndpointSource>? logger = null) {
        if (!Directory.Exists(endpointsPath)) return;


        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (string file in Directory.EnumerateFiles(endpointsPath, "*.json", opts)) {
            string relative = Path.GetRelativePath(endpointsPath, file).Replace('\\', '/').Replace(".json", "");
            try {
                _endpoints[relative] = File.ReadAllBytes(file);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                logger?.LogEndpointFileUnreadable(ex, file);
            }
        }
    }

    public int Count => _endpoints.Count;
    public int Priority => 0;

    public byte[]? Lookup(string path, string? eid) {
        string cleanPath = path.TrimEnd('/');
        while (true) {
            if (eid is not null && _endpoints.TryGetValue($"eids/{eid}/{cleanPath}", out byte[]? eidBytes))
                return eidBytes;
            if (_endpoints.TryGetValue($"default/{cleanPath}", out byte[]? defaultBytes))
                return defaultBytes;

            int lastSlash = cleanPath.LastIndexOf('/');
            int firstSlash = cleanPath.IndexOf('/');
            if (lastSlash <= firstSlash) break;
            cleanPath = cleanPath[..lastSlash];
        }

        return null;
    }
}

internal static partial class FileEndpointSourceLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Endpoint fixture {File} could not be read; it will not be served")]
    internal static partial void LogEndpointFileUnreadable(this ILogger logger, Exception ex, string file);
}

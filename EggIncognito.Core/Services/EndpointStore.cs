using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;

public sealed class EndpointStore : IEndpointStore
{
    private readonly Dictionary<string, byte[]> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<EndpointStore> _logger;

    public EndpointStore(string endpointsPath, ILogger<EndpointStore> logger)
    {
        _logger = logger;
        if (Directory.Exists(endpointsPath))
            LoadAll(endpointsPath);
        else
            _logger.LogWarning("Endpoints directory not found: {Path}", endpointsPath);
    }

    private void LoadAll(string basePath)
    {
        foreach (var file in Directory.EnumerateFiles(basePath, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(basePath, file)
                              .Replace('\\', '/')
                              .Replace(".json", "");
            try
            {
                _endpoints[relative] = Encoding.UTF8.GetBytes(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load endpoint {File}", file);
            }
        }
        _logger.LogInformation("Loaded {Count} endpoint(s) from {Path}", _endpoints.Count, basePath);
    }

    public TRes Get<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new()
    {
        var cleanPath = path.TrimEnd('/');
        while (true)
        {
            if (eid is not null && _endpoints.TryGetValue($"eids/{eid}/{cleanPath}", out var eidBytes))
                return Parse<TRes>(eidBytes);
            if (_endpoints.TryGetValue($"default/{cleanPath}", out var defaultBytes))
                return Parse<TRes>(defaultBytes);

            var lastSlash = cleanPath.LastIndexOf('/');
            var firstSlash = cleanPath.IndexOf('/');
            if (lastSlash <= firstSlash) break;
            cleanPath = cleanPath[..lastSlash];
        }
        return new TRes();
    }

    private static TRes Parse<TRes>(byte[] jsonBytes) where TRes : IMessage<TRes>, new()
    {
        var json = Encoding.UTF8.GetString(jsonBytes);
        return JsonParser.Default.Parse<TRes>(json);
    }
}

using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;

public sealed class FixtureStore : IFixtureStore
{
    private readonly Dictionary<string, byte[]> _fixtures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FixtureStore> _logger;

    public FixtureStore(string fixturesPath, ILogger<FixtureStore> logger)
    {
        _logger = logger;
        if (Directory.Exists(fixturesPath))
            LoadAll(fixturesPath);
        else
            _logger.LogWarning("Fixtures directory not found: {Path}", fixturesPath);
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
                _fixtures[relative] = Encoding.UTF8.GetBytes(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load fixture {File}", file);
            }
        }
        _logger.LogInformation("Loaded {Count} fixture(s) from {Path}", _fixtures.Count, basePath);
    }

    public TRes Get<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new()
    {
        if (eid is not null)
        {
            var eidKey = $"eids/{eid}/{Slugify(path)}";
            if (_fixtures.TryGetValue(eidKey, out var eidBytes))
                return Parse<TRes>(eidBytes);
        }

        var defaultKey = $"default/{Slugify(path)}";
        if (_fixtures.TryGetValue(defaultKey, out var defaultBytes))
            return Parse<TRes>(defaultBytes);

        return new TRes();
    }

    private static TRes Parse<TRes>(byte[] jsonBytes) where TRes : IMessage<TRes>, new()
    {
        var json = Encoding.UTF8.GetString(jsonBytes);
        return JsonParser.Default.Parse<TRes>(json);
    }

    private static string Slugify(string path) =>
        path.TrimEnd('/').Replace('/', '_');
}

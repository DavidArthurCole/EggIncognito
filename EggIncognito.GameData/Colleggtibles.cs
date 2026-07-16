using System.Reflection;
using System.Text.Json;

namespace EggIncognito.GameData;

public sealed record ColleggtibleEgg(string Identifier, int Dimension, IReadOnlyList<double> TierValues);

public interface IColleggtibleCatalog
{
    IReadOnlyList<ColleggtibleEgg> Eggs { get; }
    ColleggtibleEgg? Find(string identifier);
    IReadOnlyDictionary<string, string> ContractEggMap { get; }
    string BinaryVersion { get; }
    string Status { get; }
}

public sealed class ColleggtibleCatalog : IColleggtibleCatalog
{
    public static readonly IReadOnlyDictionary<string, int> DimensionCodes = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["INVALID"] = 0,
        ["EARNINGS"] = 1,
        ["AWAY_EARNINGS"] = 2,
        ["INTERNAL_HATCHERY_RATE"] = 3,
        ["EGG_LAYING_RATE"] = 4,
        ["SHIPPING_CAPACITY"] = 5,
        ["HAB_CAPACITY"] = 6,
        ["VEHICLE_COST"] = 7,
        ["HAB_COST"] = 8,
        ["RESEARCH_COST"] = 9
    };

    private readonly Dictionary<string, ColleggtibleEgg> _byId;

    private ColleggtibleCatalog(IReadOnlyList<ColleggtibleEgg> eggs, IReadOnlyDictionary<string, string> map, string binaryVersion, string status)
    {
        Eggs = eggs;
        ContractEggMap = map;
        BinaryVersion = binaryVersion;
        Status = status;
        _byId = eggs.ToDictionary(e => e.Identifier, StringComparer.Ordinal);
    }

    public IReadOnlyList<ColleggtibleEgg> Eggs { get; }
    public IReadOnlyDictionary<string, string> ContractEggMap { get; }
    public string BinaryVersion { get; }
    public string Status { get; }

    public ColleggtibleEgg? Find(string identifier) => _byId.GetValueOrDefault(identifier);

    public static ColleggtibleCatalog Load(string resource = "colleggtibles.json")
    {
        var file = ColleggtibleDataLoader.Read(resource);
        var eggs = file.Eggs.Select(ToEgg).ToArray();
        var map = file.ContractEggMap ?? new Dictionary<string, string>(0);
        return new ColleggtibleCatalog(eggs, map, file.BinaryVersion ?? "", file.Status ?? "");
    }

    private static ColleggtibleEgg ToEgg(ColleggtibleEggRow row)
    {
        if (string.IsNullOrEmpty(row.Identifier))
        {
            throw new GameDataSchemaException("Colleggtible row missing identifier.");
        }
        if (!DimensionCodes.TryGetValue(row.Dimension ?? "", out var code))
        {
            throw new GameDataSchemaException($"Colleggtible '{row.Identifier}' has unknown dimension '{row.Dimension}'.");
        }
        if (row.TierValues is not { Count: 4 })
        {
            throw new GameDataSchemaException($"Colleggtible '{row.Identifier}' must have exactly 4 tierValues.");
        }
        return new ColleggtibleEgg(row.Identifier, code, row.TierValues);
    }
}

public sealed record ColleggtibleEggRow(string? Identifier, string? Dimension, IReadOnlyList<double>? TierValues);

public sealed record ColleggtibleDataFile(
    string? BinaryVersion,
    string? Status,
    IReadOnlyList<ColleggtibleEggRow> Eggs,
    IReadOnlyDictionary<string, string>? ContractEggMap);

public static class ColleggtibleDataLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ColleggtibleDataFile Read(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var full = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.Ordinal))
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(full)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' unreadable.");

        return JsonSerializer.Deserialize<ColleggtibleDataFile>(stream, Options)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' parsed null.");
    }
}

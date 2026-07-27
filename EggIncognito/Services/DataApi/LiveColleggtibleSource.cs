using System.Globalization;
using System.Text.Json;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public sealed record LiveColleggtibles(
    ColleggtibleExtract Extract,
    string GameVersion,
    IReadOnlyDictionary<string, ProvenanceSource> Provenance,
    string Json);

public static class LiveColleggtibleSource {
    private static readonly JsonSerializerOptions CamelJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly Dictionary<int, string> DimensionNames =
        ColleggtibleCatalog.DimensionCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static LiveColleggtibles? Derive(IServiceProvider services, string route) {
        foreach ((string? json, string origin) in Candidates(services, route)) {
            if (json is null) continue;
            ColleggtibleExtract extract;
            try {
                extract = ColleggtibleExtractor.FromPeriodicalsJson(json);
            } catch {
                continue;
            }

            if (extract.Eggs.Count == 0) continue;
            return Build(services, route, extract, origin);
        }

        return null;
    }

    private static IEnumerable<(string? Json, string Origin)> Candidates(IServiceProvider services, string route) {
        yield return (DbJson(services, route), "db");
        yield return (DataCatalog.FixtureText(services, route), "fixture");
    }

    private static string? DbJson(IServiceProvider services, string route) {
        if (services.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db) return null;
        try {
            return db.StoredEndpoints.AsNoTracking()
                .Where(e => e.Path == route && e.Eid == null)
                .OrderByDescending(e => e.UpdatedAt)
                .Select(e => e.ResponseJson)
                .FirstOrDefault();
        } catch {
            return null;
        }
    }

    private static LiveColleggtibles Build(IServiceProvider services, string route, ColleggtibleExtract extract,
        string origin) {
        string gameVersion = services.GetService(typeof(IGameDataProvider)) is IGameDataProvider provider
            ? provider.Colleggtibles.GameVersion
            : "";
        var source = new ProvenanceSource(origin, route, "derived from captured get_periodicals");
        var provenance = new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["buffs"] = source,
            ["contractEggMap"] = source
        };
        var doc = new {
            gameVersion,
            provenance,
            eggs = extract.Eggs.Select(e => new {
                identifier = e.Identifier,
                dimension = DimensionName(e.Dimension),
                tierValues = e.TierValues
            }).ToArray(),
            contractEggMap = extract.ContractEggMap
        };
        return new LiveColleggtibles(extract, gameVersion, provenance, JsonSerializer.Serialize(doc, CamelJson));
    }

    private static string DimensionName(int code) =>
        DimensionNames.TryGetValue(code, out string? name) ? name : code.ToString(CultureInfo.InvariantCulture);
}

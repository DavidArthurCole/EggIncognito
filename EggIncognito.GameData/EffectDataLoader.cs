using System.Text.Json;
using System.Text.Json.Serialization;

namespace EggIncognito.GameData;

public sealed record EffectDataFile(
    string BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<EffectDataRow> Rows);

public sealed record EffectDataRow(
    string Id,
    EffectTarget Target,
    CombineMode CombineMode,
    double Magnitude,
    int? MaxLevel = null,
    IReadOnlyDictionary<string, JsonElement>? Meta = null);

internal static class GameDataJson {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static T Deserialize<T>(string json, string what) {
        T? value;
        try {
            value = JsonSerializer.Deserialize<T>(json, Options);
        } catch (JsonException ex) {
            throw new GameDataSchemaException($"{what} is not valid JSON: {ex.Message}");
        }

        return value ?? throw new GameDataSchemaException($"{what} parsed null.");
    }
}

public static class EffectDataLoader {
    public static EffectDataFile Parse(string json) {
        var file = GameDataJson.Deserialize<EffectDataFile>(json, "Effect data");
        if (file.Rows is null) throw new GameDataSchemaException("Effect data missing rows.");
        if (string.IsNullOrEmpty(file.BinaryVersion)) throw new GameDataSchemaException("Effect data missing binaryVersion.");
        return file;
    }

    public static IReadOnlyList<Effect> ToEffects(string family, EffectDataFile file, EffectSchema? metaSchema) {
        return file.Rows.Select(r => new Effect(
            family,
            r.Id,
            r.Target,
            r.CombineMode,
            r.Magnitude,
            r.MaxLevel,
            metaSchema,
            r.Meta is null ? null : ConvertMeta(r.Meta))).ToArray();
    }

    private static Dictionary<string, object> ConvertMeta(IReadOnlyDictionary<string, JsonElement> raw) {
        var result = new Dictionary<string, object>(raw.Count, StringComparer.Ordinal);
        foreach ((string key, var element) in raw) {
            result[key] = element.ValueKind switch {
                JsonValueKind.String => element.GetString()!,
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new GameDataSchemaException($"Unsupported meta value for '{key}': {element.ValueKind}.")
            };
        }

        return result;
    }
}

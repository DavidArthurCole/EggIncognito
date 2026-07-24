using System.Reflection;
using System.Text.Json;

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

public static class EffectDataLoader {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static EffectDataFile Read(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        var full = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.Ordinal))
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(full)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' unreadable.");

        return JsonSerializer.Deserialize<EffectDataFile>(stream, Options)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' parsed null.");
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
        foreach (var (key, element) in raw) {
            result[key] = element.ValueKind switch {
                JsonValueKind.String => element.GetString()!,
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new GameDataSchemaException($"Unsupported meta value for '{key}': {element.ValueKind}.")
            };
        }
        return result;
    }
}

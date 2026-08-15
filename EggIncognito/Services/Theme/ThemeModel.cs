using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Theme;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ThemeTokenValue(
    [property: JsonPropertyName("hex")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Hex = null,
    [property: JsonPropertyName("l")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? L = null,
    [property: JsonPropertyName("c")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? C = null,
    [property: JsonPropertyName("h")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? H = null) {
    public ThemeColor? Resolve() {
        if (Hex is not null) return L is null && C is null && H is null ? ThemeColor.FromHex(Hex) : null;
        if (L is { } l && C is { } c && H is { } h) return ThemeColor.FromOklch(l, c, h);
        return null;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ThemeHueRotate(
    [property: JsonPropertyName("enabled")] bool Enabled = false,
    [property: JsonPropertyName("seconds")] double Seconds = 30) {
    public ThemeHueRotate Clamped() => new(Enabled, Math.Clamp(Seconds, 6, 120));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ThemeChroma(
    [property: JsonPropertyName("surfaceTint")] double SurfaceTint = 0,
    [property: JsonPropertyName("gradientHueShift")] double GradientHueShift = 0,
    [property: JsonPropertyName("glowRadius")] double GlowRadius = 0,
    [property: JsonPropertyName("glowAlpha")] double GlowAlpha = 0,
    [property: JsonPropertyName("hueRotate")] ThemeHueRotate? HueRotate = null) {
    public ThemeChroma Clamped() => new(
        Math.Clamp(SurfaceTint, 0, 12),
        Math.Clamp(GradientHueShift, -60, 60),
        Math.Clamp(GlowRadius, 0, 24),
        Math.Clamp(GlowAlpha, 0, 60),
        (HueRotate ?? new ThemeHueRotate()).Clamped());

    public static readonly ThemeChroma None = new(HueRotate: new ThemeHueRotate());
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed partial record ThemeModel(
    [property: JsonPropertyName("$schema")] string Schema,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tokens")] IReadOnlyDictionary<string, ThemeTokenValue> Tokens,
    [property: JsonPropertyName("chroma")] ThemeChroma Chroma,
    [property: JsonPropertyName("css")] string Css) {
    public const string SchemaId = "egi-theme/1";
    public const int CurrentSchemaVersion = 1;
    public const int MaxCssSourceBytes = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    public ThemeColor ResolveToken(string name) {
        if (Tokens.TryGetValue(name, out var value) && value.Resolve() is { } color) return color;
        return ThemePresets.DefaultToken(name);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static (ThemeModel? Model, IReadOnlyList<string> Errors) Parse(string json) {
        var errors = new List<string>();
        ThemeModel? raw;
        try {
            raw = JsonSerializer.Deserialize<ThemeModel>(json, JsonOptions);
        } catch (JsonException ex) {
            return (null, [ex.Message]);
        }

        if (raw is null) return (null, ["empty document"]);
        if (raw.Schema != SchemaId) errors.Add($"unknown $schema, expected {SchemaId}");
        if (raw.SchemaVersion != CurrentSchemaVersion)
            errors.Add($"unknown schemaVersion {raw.SchemaVersion}, expected {CurrentSchemaVersion}");
        if (string.IsNullOrWhiteSpace(raw.Name) || raw.Name.Length > 64) errors.Add("name must be 1 to 64 chars");
        if (raw.Slug is null || !SlugPattern().IsMatch(raw.Slug)) errors.Add("slug must match [a-z0-9-]{1,64}");

        var tokens = new Dictionary<string, ThemeTokenValue>(StringComparer.Ordinal);
        foreach (var (key, value) in raw.Tokens ?? new Dictionary<string, ThemeTokenValue>()) {
            if (ThemeTokens.CanonicalSettable(key) is not { } canonical) {
                errors.Add($"unknown token '{key}'");
                continue;
            }

            if (value?.Resolve() is null) {
                errors.Add($"token '{key}' must be a hex value or an oklch triple");
                continue;
            }

            tokens[canonical] = value;
        }

        string css = raw.Css ?? "";
        if (System.Text.Encoding.UTF8.GetByteCount(css) > MaxCssSourceBytes)
            errors.Add($"css source over {MaxCssSourceBytes / 1024} KB");

        if (errors.Count > 0) return (null, errors);
        return (raw with { Tokens = tokens, Chroma = (raw.Chroma ?? ThemeChroma.None).Clamped(), Css = css }, errors);
    }

    [GeneratedRegex("^[a-z0-9-]{1,64}$", RegexOptions.Compiled)]
    private static partial Regex SlugPattern();
}

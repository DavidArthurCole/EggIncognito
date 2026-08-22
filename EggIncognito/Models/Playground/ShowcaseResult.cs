namespace EggIncognito.Models.Playground;

public record ShowcaseResult(bool Ok, int Count, PresetRow[]? Presets, string? Diagnostics);

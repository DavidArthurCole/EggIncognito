using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public static class ThemePalette {
    public static ThemeColor TokenOrDefault(this ThemeModel model, string name) =>
        model.ResolveToken(name) ?? ThemePresets.DefaultToken(name);

    public static IReadOnlyDictionary<string, ThemeColor> Colors(ThemeModel model) {
        var colors = new Dictionary<string, ThemeColor>(StringComparer.Ordinal);
        foreach (string name in ThemeTokens.Settable) colors[name] = model.TokenOrDefault(name);
        return colors;
    }

    public static ContrastResult Contrast(ThemeModel model) =>
        ThemeContrast.Validate(Colors(model), model.Chroma, ThemeTokens.StatusTokens);
}

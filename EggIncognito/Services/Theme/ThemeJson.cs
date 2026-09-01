using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public static class ThemeJson {
    public static (ThemeModel? Model, IReadOnlyList<string> Errors) Parse(string json) {
        var (model, errors) = ThemeModel.Parse(json, ThemeTokens.Registry);
        if (model is null) return (null, errors);

        foreach (string key in model.Tokens.Keys) {
            if (!ThemeTokens.IsSettable(key)) return (null, [$"unknown token '{key}'"]);
        }

        return (model, errors);
    }
}

using EggIdentity.Styles.Theming;

namespace EggIncognito.Services.Theme;

public static class ThemeCss {
    public static CssParseResult Parse(string css) {
        var result = ThemeCssParser.Parse(css, ThemeTokens.Catalog, ThemeTokens.Registry, ThemeModel.MaxCssSourceBytes);
        if (!result.Ok) return result;

        foreach (var rule in result.Rules) {
            foreach (var decl in rule.Declarations) {
                foreach (var group in decl.Groups) {
                    foreach (var part in group) {
                        if (FindViolation(part) is { } name)
                            return new CssParseResult([], [new CssError(1, 1, $"token '{name}' is not settable")]);
                    }
                }
            }
        }

        return result;
    }

    private static string? FindViolation(CssPart part) {
        if (part is not CssFunc fn) return null;
        if (fn.Name == "var" && fn.Args.Count == 1 && fn.Args[0].Count == 1 &&
            fn.Args[0][0] is CssKeyword { Text: var text } && text.StartsWith("--color-", StringComparison.Ordinal)) {
            string name = text["--color-".Length..];
            return ThemeTokens.IsSettable(name) ? null : name;
        }

        foreach (var group in fn.Args) {
            foreach (var inner in group) {
                if (FindViolation(inner) is { } violation) return violation;
            }
        }

        return null;
    }
}

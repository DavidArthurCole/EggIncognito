using System.Globalization;
using System.Text;

namespace EggIncognito.Services.Theme;

public abstract record CssPart;

public sealed record CssKeyword(string Text) : CssPart;

public sealed record CssNumber(double Value, string Unit) : CssPart;

public sealed record CssHex(byte R, byte G, byte B, byte? A) : CssPart;

public sealed record CssFunc(string Name, IReadOnlyList<IReadOnlyList<CssPart>> Args) : CssPart;

public sealed record CssDeclaration(string Property, IReadOnlyList<IReadOnlyList<CssPart>> Groups);

public sealed record CssRule(ThemeCatalogEntry Entry, IReadOnlyList<CssDeclaration> Declarations);

public sealed record CssError(int Line, int Col, string Message);

public sealed record CssParseResult(IReadOnlyList<CssRule> Rules, IReadOnlyList<CssError> Errors) {
    public bool Ok => Errors.Count == 0;
}

public static class ThemeCssParser {
    private enum TokKind {
        Ident,
        Number,
        Hash,
        LParen,
        RParen,
        LBrace,
        RBrace,
        Colon,
        Semicolon,
        Comma,
        End
    }

    private sealed record Tok(TokKind Kind, string Text, double Num, string Unit, int Line, int Col);

    private static readonly string[] Units = ["", "px", "em", "ms", "s", "deg", "%"];

#pragma warning disable IDE0028
    private static readonly HashSet<string> NamedColors = new(StringComparer.Ordinal) {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black",
        "blanchedalmond", "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse",
        "chocolate", "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan",
        "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta",
        "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon", "darkseagreen",
        "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink",
        "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite", "forestgreen",
        "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow",
        "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
        "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan",
        "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink", "lightsalmon",
        "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue",
        "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine",
        "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue",
        "mediumspringgreen", "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream",
        "mistyrose", "moccasin", "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange",
        "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise", "palevioletred",
        "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "rebeccapurple",
        "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell",
        "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen",
        "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat", "white",
        "whitesmoke", "yellow", "yellowgreen"
    };

    private static readonly HashSet<string> TransitionTargets = new(StringComparer.Ordinal) {
        "color", "background-color", "border-color", "box-shadow", "opacity", "outline-color"
    };
#pragma warning restore IDE0028

    private static readonly string[] BorderStyles = ["none", "solid", "dashed", "dotted", "double"];
    private static readonly string[] FontStyles = ["normal", "italic"];
    private static readonly string[] FontWeightKeywords = ["normal", "bold"];
    private static readonly string[] TextTransforms = ["none", "uppercase", "lowercase", "capitalize"];
    private static readonly string[] DecorationLines = ["none", "underline", "overline", "line-through"];
    private static readonly string[] GradientSides = ["left", "right", "top", "bottom"];
    private static readonly string[] GradientShapes = ["circle", "ellipse"];

    public static CssParseResult Parse(string source) {
        var errors = new List<CssError>();
        if (Encoding.UTF8.GetByteCount(source) > ThemeModel.MaxCssSourceBytes) {
            errors.Add(new CssError(1, 1, $"source over {ThemeModel.MaxCssSourceBytes / 1024} KB"));
            return new CssParseResult([], errors);
        }

        for (int i = 0; i < source.Length; i++) {
            char c = source[i];
            if (c == '\\') {
                errors.Add(At(source, i, "backslash is not allowed anywhere"));
                return new CssParseResult([], errors);
            }

            if (c is '\t' or '\r' or '\n' or (>= ' ' and <= '~')) continue;
            errors.Add(At(source, i, $"non-ASCII character U+{(int)c:X4} is not allowed"));
            return new CssParseResult([], errors);
        }

        var toks = Tokenize(source, errors);
        if (errors.Count > 0) return new CssParseResult([], errors);

        var rules = new List<CssRule>();
        int p = 0;
        while (toks[p].Kind != TokKind.End) {
            var rule = ParseRule(toks, ref p, errors);
            if (rule is null) break;
            rules.Add(rule);
        }

        return new CssParseResult(errors.Count == 0 ? rules : [], errors);
    }

    private static CssError At(string source, int index, string message) {
        int line = 1;
        int col = 1;
        for (int i = 0; i < index && i < source.Length; i++) {
            if (source[i] == '\n') {
                line++;
                col = 1;
            } else {
                col++;
            }
        }

        return new CssError(line, col, message);
    }

    private static List<Tok> Tokenize(string s, List<CssError> errors) {
        var toks = new List<Tok>();
        int line = 1;
        int col = 1;
        int i = 0;

        void Advance(int n) {
            for (int k = 0; k < n; k++) {
                if (s[i + k] == '\n') {
                    line++;
                    col = 1;
                } else {
                    col++;
                }
            }

            i += n;
        }

        while (i < s.Length) {
            char c = s[i];
            if (c is ' ' or '\t' or '\r' or '\n') {
                Advance(1);
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') {
                int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0) {
                    errors.Add(new CssError(line, col, "unterminated comment"));
                    return toks;
                }

                Advance(close + 2 - i);
                continue;
            }

            int startLine = line;
            int startCol = col;
            if (c == '-' && i + 1 < s.Length && (char.IsAsciiDigit(s[i + 1]) || s[i + 1] == '.') ||
                char.IsAsciiDigit(c) || c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])) {
                int j = i;
                if (s[j] == '-') j++;
                while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
                if (j < s.Length && s[j] == '.') {
                    j++;
                    if (j >= s.Length || !char.IsAsciiDigit(s[j])) {
                        errors.Add(new CssError(startLine, startCol, "malformed number"));
                        return toks;
                    }

                    while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
                }

                int unitStart = j;
                if (j < s.Length && s[j] == '%') {
                    j++;
                } else {
                    while (j < s.Length && char.IsAsciiLetter(s[j])) j++;
                }

                string numText = s[i..unitStart];
                string unitText = s[unitStart..j].ToLowerInvariant();
                if (Array.IndexOf(Units, unitText) < 0) {
                    errors.Add(new CssError(startLine, startCol, $"unknown unit '{unitText}'"));
                    return toks;
                }

                double value = double.Parse(numText, NumberStyles.Float, CultureInfo.InvariantCulture);
                toks.Add(new Tok(TokKind.Number, numText, value, Units[Array.IndexOf(Units, unitText)],
                    startLine, startCol));
                Advance(j - i);
                continue;
            }

            if (char.IsAsciiLetter(c) || c is '-' or '_') {
                int j = i;
                while (j < s.Length && (char.IsAsciiLetterOrDigit(s[j]) || s[j] is '-' or '_')) j++;
                toks.Add(new Tok(TokKind.Ident, s[i..j].ToLowerInvariant(), 0, "", startLine, startCol));
                Advance(j - i);
                continue;
            }

            if (c == '#') {
                int j = i + 1;
                while (j < s.Length && Uri.IsHexDigit(s[j])) j++;
                toks.Add(new Tok(TokKind.Hash, s[(i + 1)..j].ToLowerInvariant(), 0, "", startLine, startCol));
                Advance(j - i);
                continue;
            }

            TokKind? kind = c switch {
                '(' => TokKind.LParen,
                ')' => TokKind.RParen,
                '{' => TokKind.LBrace,
                '}' => TokKind.RBrace,
                ':' => TokKind.Colon,
                ';' => TokKind.Semicolon,
                ',' => TokKind.Comma,
                _ => null
            };
            if (kind is null) {
                errors.Add(new CssError(startLine, startCol, $"unexpected character '{c}'"));
                return toks;
            }

            toks.Add(new Tok(kind.Value, c.ToString(), 0, "", startLine, startCol));
            Advance(1);
        }

        toks.Add(new Tok(TokKind.End, "", 0, "", line, col));
        return toks;
    }

    private static CssRule? ParseRule(List<Tok> toks, ref int p, List<CssError> errors) {
        var name = toks[p];
        if (name.Kind != TokKind.Ident) {
            errors.Add(new CssError(name.Line, name.Col, "expected a surface name"));
            return null;
        }

        var entry = ThemeCssCatalog.Find(name.Text);
        if (entry is null) {
            errors.Add(new CssError(name.Line, name.Col, $"unknown surface '{name.Text}'"));
            return null;
        }

        p++;
        if (toks[p].Kind != TokKind.LBrace) {
            errors.Add(new CssError(toks[p].Line, toks[p].Col, "expected '{' after the surface name"));
            return null;
        }

        p++;
        var decls = new List<CssDeclaration>();
        while (true) {
            while (toks[p].Kind == TokKind.Semicolon) p++;
            if (toks[p].Kind == TokKind.RBrace) {
                p++;
                return new CssRule(entry, decls);
            }

            if (toks[p].Kind == TokKind.End) {
                errors.Add(new CssError(toks[p].Line, toks[p].Col, "unbalanced braces: missing '}'"));
                return null;
            }

            var decl = ParseDeclaration(entry, toks, ref p, errors);
            if (decl is null) return null;
            decls.Add(decl);
        }
    }

    private static CssDeclaration? ParseDeclaration(ThemeCatalogEntry entry, List<Tok> toks, ref int p,
        List<CssError> errors) {
        var prop = toks[p];
        if (prop.Kind != TokKind.Ident) {
            errors.Add(new CssError(prop.Line, prop.Col, "expected a property name"));
            return null;
        }

        string? canonical = ThemeCssCatalog.CanonicalProperty(prop.Text);
        if (canonical is null) {
            errors.Add(new CssError(prop.Line, prop.Col, $"unknown property '{prop.Text}'"));
            return null;
        }

        if (!ThemeCssCatalog.Allows(entry.Group, canonical)) {
            errors.Add(new CssError(prop.Line, prop.Col,
                $"property '{canonical}' is not allowed on surface '{entry.Name}'"));
            return null;
        }

        p++;
        if (toks[p].Kind != TokKind.Colon) {
            errors.Add(new CssError(toks[p].Line, toks[p].Col, "expected ':'"));
            return null;
        }

        p++;
        var groups = ParseValue(canonical, toks, ref p, errors);
        if (groups is null) return null;
        if (toks[p].Kind is not (TokKind.Semicolon or TokKind.RBrace)) {
            errors.Add(new CssError(toks[p].Line, toks[p].Col, "expected ';' or '}' after the value"));
            return null;
        }

        return new CssDeclaration(canonical, groups);
    }

    private static IReadOnlyList<IReadOnlyList<CssPart>>? ParseValue(string property, List<Tok> toks, ref int p,
        List<CssError> errors) {
        switch (property) {
            case "color":
            case "background-color":
            case "border-color":
            case "text-decoration-color":
            case "outline-color":
            case "caret-color":
            case "accent-color": {
                    var color = ParseColor(toks, ref p, errors);
                    return color is null ? null : [[color]];
                }
            case "border-width": {
                    var len = ParseLength(toks, ref p, errors, 0, 4);
                    return len is null ? null : [[len]];
                }
            case "border-style":
                return Keyword(toks, ref p, errors, BorderStyles);
            case "font-style":
                return Keyword(toks, ref p, errors, FontStyles);
            case "text-transform":
                return Keyword(toks, ref p, errors, TextTransforms);
            case "text-decoration-line":
                return Keyword(toks, ref p, errors, DecorationLines);
            case "border-radius": {
                    var parts = new List<CssPart>();
                    while (parts.Count < 4 && toks[p].Kind == TokKind.Number) {
                        var len = ParseLength(toks, ref p, errors, 0, 32);
                        if (len is null) return null;
                        parts.Add(len);
                    }

                    if (parts.Count == 0) {
                        errors.Add(Expected(toks[p], "a length"));
                        return null;
                    }

                    return [parts];
                }
            case "font-weight": {
                    if (toks[p].Kind == TokKind.Ident && Array.IndexOf(FontWeightKeywords, toks[p].Text) >= 0) {
                        string kw = FontWeightKeywords[Array.IndexOf(FontWeightKeywords, toks[p].Text)];
                        p++;
                        return [[new CssKeyword(kw)]];
                    }

                    if (toks[p].Kind == TokKind.Number && toks[p].Unit == "") {
                        double w = Math.Clamp(toks[p].Num, 100, 900);
                        p++;
                        return [[new CssNumber(Math.Round(w), "")]];
                    }

                    errors.Add(Expected(toks[p], "a weight"));
                    return null;
                }
            case "letter-spacing": {
                    if (toks[p].Kind == TokKind.Number && toks[p].Unit == "em") {
                        double v = Math.Clamp(toks[p].Num, -0.05, 0.2);
                        p++;
                        return [[new CssNumber(v, "em")]];
                    }

                    errors.Add(Expected(toks[p], "an em length"));
                    return null;
                }
            case "opacity": {
                    if (toks[p].Kind == TokKind.Number && toks[p].Unit == "") {
                        double v = Math.Clamp(toks[p].Num, 0.35, 1);
                        p++;
                        return [[new CssNumber(v, "")]];
                    }

                    errors.Add(Expected(toks[p], "a number"));
                    return null;
                }
            case "transition-duration": {
                    if (toks[p].Kind == TokKind.Number && toks[p].Unit is "ms" or "s") {
                        double ms = toks[p].Unit == "s" ? toks[p].Num * 1000 : toks[p].Num;
                        ms = Math.Clamp(ms, 0, 1000);
                        p++;
                        return [[new CssNumber(Math.Round(ms), "ms")]];
                    }

                    errors.Add(Expected(toks[p], "a duration"));
                    return null;
                }
            case "transition-property": {
                    var groups = new List<IReadOnlyList<CssPart>>();
                    while (true) {
                        if (toks[p].Kind != TokKind.Ident || !TransitionTargets.TryGetValue(toks[p].Text, out string? t)) {
                            errors.Add(Expected(toks[p], "an animatable property name"));
                            return null;
                        }

                        groups.Add([new CssKeyword(t)]);
                        p++;
                        if (toks[p].Kind != TokKind.Comma) return groups;
                        p++;
                    }
                }
            case "box-shadow": {
                    if (toks[p].Kind == TokKind.Ident && toks[p].Text == "none") {
                        p++;
                        return [[new CssKeyword("none")]];
                    }

                    var groups = new List<IReadOnlyList<CssPart>>();
                    while (true) {
                        var shadow = ParseShadow(toks, ref p, errors);
                        if (shadow is null) return null;
                        groups.Add(shadow);
                        if (toks[p].Kind != TokKind.Comma) break;
                        p++;
                    }

                    if (groups.Count > 2) {
                        errors.Add(Expected(toks[p], "at most 2 shadows"));
                        return null;
                    }

                    return groups;
                }
            case "background-image": {
                    if (toks[p].Kind == TokKind.Ident && toks[p].Text == "none") {
                        p++;
                        return [[new CssKeyword("none")]];
                    }

                    var grad = ParseGradient(toks, ref p, errors);
                    return grad is null ? null : [[grad]];
                }
            default:
                errors.Add(Expected(toks[p], "a supported property"));
                return null;
        }
    }

    private static IReadOnlyList<IReadOnlyList<CssPart>>? Keyword(List<Tok> toks, ref int p, List<CssError> errors,
        string[] allowed) {
        if (toks[p].Kind == TokKind.Ident && Array.IndexOf(allowed, toks[p].Text) >= 0) {
            string kw = allowed[Array.IndexOf(allowed, toks[p].Text)];
            p++;
            return [[new CssKeyword(kw)]];
        }

        errors.Add(Expected(toks[p], $"one of: {string.Join(", ", allowed)}"));
        return null;
    }

    private static CssNumber? ParseLength(List<Tok> toks, ref int p, List<CssError> errors, double min, double max) {
        if (toks[p].Kind == TokKind.Number && (toks[p].Unit == "px" || toks[p].Unit == "" && toks[p].Num == 0)) {
            double v = Math.Clamp(toks[p].Num, min, max);
            p++;
            return new CssNumber(v, "px");
        }

        errors.Add(Expected(toks[p], "a px length"));
        return null;
    }

    private static List<CssPart>? ParseShadow(List<Tok> toks, ref int p, List<CssError> errors) {
        var parts = new List<CssPart>();
        if (toks[p].Kind == TokKind.Ident && toks[p].Text == "inset") {
            parts.Add(new CssKeyword("inset"));
            p++;
        }

        var lengths = new List<CssPart>();
        while (lengths.Count < 4 && toks[p].Kind == TokKind.Number) {
            double min = lengths.Count == 2 ? 0 : -32;
            var len = ParseLength(toks, ref p, errors, min, 32);
            if (len is null) return null;
            lengths.Add(len);
        }

        if (lengths.Count < 2) {
            errors.Add(Expected(toks[p], "at least two shadow lengths"));
            return null;
        }

        parts.AddRange(lengths);
        var color = ParseColor(toks, ref p, errors);
        if (color is null) return null;
        parts.Add(color);
        return parts;
    }

    private static CssFunc? ParseGradient(List<Tok> toks, ref int p, List<CssError> errors) {
        if (toks[p].Kind != TokKind.Ident || toks[p].Text is not ("linear-gradient" or "radial-gradient")) {
            errors.Add(Expected(toks[p], "linear-gradient or radial-gradient"));
            return null;
        }

        string fn = toks[p].Text == "linear-gradient" ? "linear-gradient" : "radial-gradient";
        p++;
        if (toks[p].Kind != TokKind.LParen) {
            errors.Add(Expected(toks[p], "'('"));
            return null;
        }

        p++;
        var args = new List<IReadOnlyList<CssPart>>();

        if (fn == "linear-gradient" && toks[p].Kind == TokKind.Number && toks[p].Unit == "deg") {
            double angle = toks[p].Num % 360;
            if (angle < 0) angle += 360;
            args.Add([new CssNumber(angle, "deg")]);
            p++;
            if (!Eat(toks, ref p, TokKind.Comma, errors)) return null;
        } else if (fn == "linear-gradient" && toks[p].Kind == TokKind.Ident && toks[p].Text == "to") {
            var dir = new List<CssPart> { new CssKeyword("to") };
            p++;
            int sides = 0;
            while (sides < 2 && toks[p].Kind == TokKind.Ident &&
                   Array.IndexOf(GradientSides, toks[p].Text) >= 0) {
                dir.Add(new CssKeyword(GradientSides[Array.IndexOf(GradientSides, toks[p].Text)]));
                p++;
                sides++;
            }

            if (sides == 0) {
                errors.Add(Expected(toks[p], "a gradient side"));
                return null;
            }

            args.Add(dir);
            if (!Eat(toks, ref p, TokKind.Comma, errors)) return null;
        } else if (fn == "radial-gradient" && toks[p].Kind == TokKind.Ident &&
                   Array.IndexOf(GradientShapes, toks[p].Text) >= 0) {
            args.Add([new CssKeyword(GradientShapes[Array.IndexOf(GradientShapes, toks[p].Text)])]);
            p++;
            if (!Eat(toks, ref p, TokKind.Comma, errors)) return null;
        }

        int stops = 0;
        while (true) {
            var color = ParseColor(toks, ref p, errors);
            if (color is null) return null;
            var stop = new List<CssPart> { color };
            if (toks[p].Kind == TokKind.Number && toks[p].Unit == "%") {
                stop.Add(new CssNumber(Math.Clamp(toks[p].Num, 0, 100), "%"));
                p++;
            }

            args.Add(stop);
            stops++;
            if (toks[p].Kind != TokKind.Comma) break;
            p++;
        }

        if (stops is < 2 or > 8) {
            errors.Add(Expected(toks[p], "2 to 8 gradient stops"));
            return null;
        }

        if (toks[p].Kind != TokKind.RParen) {
            errors.Add(Expected(toks[p], "')'"));
            return null;
        }

        p++;
        return new CssFunc(fn, args);
    }

    private static CssPart? ParseColor(List<Tok> toks, ref int p, List<CssError> errors) {
        var t = toks[p];
        if (t.Kind == TokKind.Hash) {
            if (t.Text.Length is 3 or 6 or 8) {
                string h = t.Text.Length == 3
                    ? $"{t.Text[0]}{t.Text[0]}{t.Text[1]}{t.Text[1]}{t.Text[2]}{t.Text[2]}"
                    : t.Text;
                byte r = byte.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte? a = h.Length == 8
                    ? byte.Parse(h[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : null;
                p++;
                return new CssHex(r, g, b, a);
            }

            errors.Add(new CssError(t.Line, t.Col, "hex color must be 3, 6 or 8 digits"));
            return null;
        }

        if (t.Kind != TokKind.Ident) {
            errors.Add(Expected(t, "a color"));
            return null;
        }

        if (toks[p + 1].Kind != TokKind.LParen) {
            if (t.Text == "transparent") {
                p++;
                return new CssKeyword("transparent");
            }

            if (t.Text == "currentcolor") {
                p++;
                return new CssKeyword("currentColor");
            }

            if (NamedColors.TryGetValue(t.Text, out string? named)) {
                p++;
                return new CssKeyword(named);
            }

            errors.Add(Expected(t, "a color"));
            return null;
        }

        string fn = t.Text;
        p += 2;
        switch (fn) {
            case "var": {
                    if (toks[p].Kind == TokKind.Ident && toks[p].Text.StartsWith("--color-", StringComparison.Ordinal) &&
                        ThemeTokens.CanonicalSettable(toks[p].Text["--color-".Length..]) is { } token) {
                        p++;
                        if (!Eat(toks, ref p, TokKind.RParen, errors)) return null;
                        return new CssFunc("var", [[new CssKeyword("--color-" + token)]]);
                    }

                    errors.Add(Expected(toks[p], "a settable --color-* token"));
                    return null;
                }
            case "rgb":
            case "rgba": {
                    var channels = new List<CssPart>();
                    for (int k = 0; k < 3; k++) {
                        if (toks[p].Kind != TokKind.Number || toks[p].Unit is not ("" or "%")) {
                            errors.Add(Expected(toks[p], "a channel value"));
                            return null;
                        }

                        double v = toks[p].Unit == "%"
                            ? Math.Round(Math.Clamp(toks[p].Num, 0, 100) * 2.55)
                            : Math.Round(Math.Clamp(toks[p].Num, 0, 255));
                        channels.Add(new CssNumber(v, ""));
                        p++;
                        if (k < 2 && !Eat(toks, ref p, TokKind.Comma, errors)) return null;
                    }

                    var argGroups = channels.Select(IReadOnlyList<CssPart> (c) => [c]).ToList();
                    if (fn == "rgba") {
                        if (!Eat(toks, ref p, TokKind.Comma, errors)) return null;
                        if (toks[p].Kind != TokKind.Number || toks[p].Unit != "") {
                            errors.Add(Expected(toks[p], "an alpha value"));
                            return null;
                        }

                        argGroups.Add([new CssNumber(Math.Clamp(toks[p].Num, 0, 1), "")]);
                        p++;
                    }

                    if (!Eat(toks, ref p, TokKind.RParen, errors)) return null;
                    return new CssFunc(fn == "rgba" ? "rgba" : "rgb", argGroups);
                }
            case "hsl": {
                    if (toks[p].Kind != TokKind.Number || toks[p].Unit is not ("" or "deg")) {
                        errors.Add(Expected(toks[p], "a hue"));
                        return null;
                    }

                    double h = toks[p].Num % 360;
                    if (h < 0) h += 360;
                    p++;
                    List<IReadOnlyList<CssPart>> groups = [[new CssNumber(h, "")]];
                    for (int k = 0; k < 2; k++) {
                        if (!Eat(toks, ref p, TokKind.Comma, errors)) return null;
                        if (toks[p].Kind != TokKind.Number || toks[p].Unit != "%") {
                            errors.Add(Expected(toks[p], "a percentage"));
                            return null;
                        }

                        groups.Add([new CssNumber(Math.Clamp(toks[p].Num, 0, 100), "%")]);
                        p++;
                    }

                    if (!Eat(toks, ref p, TokKind.RParen, errors)) return null;
                    return new CssFunc("hsl", groups);
                }
            case "oklch": {
                    if (toks[p].Kind != TokKind.Number || toks[p].Unit is not ("" or "%")) {
                        errors.Add(Expected(toks[p], "a lightness"));
                        return null;
                    }

                    double l = toks[p].Unit == "%" ? Math.Clamp(toks[p].Num, 0, 100) : Math.Clamp(toks[p].Num, 0, 1) * 100;
                    p++;
                    if (toks[p].Kind != TokKind.Number || toks[p].Unit != "") {
                        errors.Add(Expected(toks[p], "a chroma"));
                        return null;
                    }

                    double c = Math.Clamp(toks[p].Num, 0, 0.5);
                    p++;
                    if (toks[p].Kind != TokKind.Number || toks[p].Unit is not ("" or "deg")) {
                        errors.Add(Expected(toks[p], "a hue"));
                        return null;
                    }

                    double h = toks[p].Num % 360;
                    if (h < 0) h += 360;
                    p++;
                    if (!Eat(toks, ref p, TokKind.RParen, errors)) return null;
                    return new CssFunc("oklch", [[new CssNumber(l, "%")], [new CssNumber(c, "")], [new CssNumber(h, "")]]);
                }
            case "oklab": {
                    if (toks[p].Kind != TokKind.Number || toks[p].Unit is not ("" or "%")) {
                        errors.Add(Expected(toks[p], "a lightness"));
                        return null;
                    }

                    double l = toks[p].Unit == "%" ? Math.Clamp(toks[p].Num, 0, 100) : Math.Clamp(toks[p].Num, 0, 1) * 100;
                    p++;
                    List<IReadOnlyList<CssPart>> ab = [[new CssNumber(l, "%")]];
                    for (int k = 0; k < 2; k++) {
                        if (toks[p].Kind != TokKind.Number || toks[p].Unit != "") {
                            errors.Add(Expected(toks[p], "an oklab component"));
                            return null;
                        }

                        ab.Add([new CssNumber(Math.Clamp(toks[p].Num, -0.4, 0.4), "")]);
                        p++;
                    }

                    if (!Eat(toks, ref p, TokKind.RParen, errors)) return null;
                    return new CssFunc("oklab", ab);
                }
            case "color-mix": {
                    if (toks[p].Kind != TokKind.Ident || toks[p].Text != "in") {
                        errors.Add(Expected(toks[p], "'in oklab'"));
                        return null;
                    }

                    p++;
                    if (toks[p].Kind != TokKind.Ident || toks[p].Text != "oklab") {
                        errors.Add(Expected(toks[p], "'in oklab'"));
                        return null;
                    }

                    p++;
                    if (!Eat(toks, ref p, TokKind.Comma, errors)) return null;
                    List<IReadOnlyList<CssPart>> args = [[new CssKeyword("in"), new CssKeyword("oklab")]];
                    for (int k = 0; k < 2; k++) {
                        var color = ParseColor(toks, ref p, errors);
                        if (color is null) return null;
                        var group = new List<CssPart> { color };
                        if (toks[p].Kind == TokKind.Number && toks[p].Unit == "%") {
                            group.Add(new CssNumber(Math.Clamp(toks[p].Num, 0, 100), "%"));
                            p++;
                        }

                        args.Add(group);
                        if (k == 0 && !Eat(toks, ref p, TokKind.Comma, errors)) return null;
                    }

                    if (!Eat(toks, ref p, TokKind.RParen, errors)) return null;
                    return new CssFunc("color-mix", args);
                }
            default:
                errors.Add(new CssError(t.Line, t.Col, $"unknown function '{fn}'"));
                return null;
        }
    }

    private static bool Eat(List<Tok> toks, ref int p, TokKind kind, List<CssError> errors) {
        if (toks[p].Kind == kind) {
            p++;
            return true;
        }

        errors.Add(Expected(toks[p], kind switch {
            TokKind.Comma => "','",
            TokKind.RParen => "')'",
            _ => kind.ToString()
        }));
        return false;
    }

    private static CssError Expected(Tok t, string what) =>
        new(t.Line, t.Col, $"expected {what}" + (t.Kind == TokKind.End ? " but reached the end" : $" at '{t.Text}'"));

    internal static string FormatNumber(double v) {
        double rounded = Math.Round(v, 4);
        return rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

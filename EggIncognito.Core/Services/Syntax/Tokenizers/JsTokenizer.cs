namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class JsTokenizer : ISyntaxTokenizer {
    private const byte Normal = 0;
    private const byte InBlockComment = 1;
    private const byte InTemplate = 2;

    private static readonly string[] Keywords = [
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete",
        "do", "else", "export", "extends", "finally", "for", "function", "if", "import", "in",
        "instanceof", "let", "new", "of", "return", "static", "super", "switch", "this", "throw",
        "try", "typeof", "var", "void", "while", "with", "yield", "async", "get", "set"
    ];

    private static readonly string[] Types = [
        "Array", "Boolean", "Date", "Error", "JSON", "Map", "Math", "Number", "Object", "Promise",
        "RegExp", "Set", "String", "Symbol", "BigInt"
    ];

    public string Id => "js";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        int i = 0;
        if (state == InBlockComment) {
            int close = line.IndexOf("*/", StringComparison.Ordinal);
            if (close < 0) {
                ScanUtil.Add(sink, 0, line.Length, TokenKind.Comment);
                return InBlockComment;
            }

            ScanUtil.Add(sink, 0, close + 2, TokenKind.Comment);
            i = close + 2;
        } else if (state == InTemplate) {
            int close = TemplateEnd(line, 0);
            if (close < 0) {
                ScanUtil.Add(sink, 0, line.Length, TokenKind.String);
                return InTemplate;
            }

            ScanUtil.Add(sink, 0, close, TokenKind.String);
            i = close;
        }

        while (i < line.Length) {
            char c = line[i];
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') {
                ScanUtil.Add(sink, i, line.Length - i, TokenKind.Comment);
                return Normal;
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '*') {
                int close = line[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                if (close < 0) {
                    ScanUtil.Add(sink, i, line.Length - i, TokenKind.Comment);
                    return InBlockComment;
                }

                ScanUtil.Add(sink, i, close + 4, TokenKind.Comment);
                i += close + 4;
                continue;
            }

            if (c is '"' or '\'') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            if (c == '`') {
                int close = TemplateEnd(line, i + 1);
                if (close < 0) {
                    ScanUtil.Add(sink, i, line.Length - i, TokenKind.String);
                    return InTemplate;
                }

                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            if (char.IsAsciiDigit(c)) {
                int end = ScanUtil.ReadNumber(line, i);
                if (end == i) end++;
                ScanUtil.Add(sink, i, end - i, TokenKind.Number);
                i = end;
                continue;
            }

            if (ScanUtil.IsIdentStart(c)) {
                int end = ScanUtil.ReadIdent(line, i);
                var word = line[i..end];
                TokenKind kind;
                if (word.SequenceEqual("true") || word.SequenceEqual("false")) {
                    kind = TokenKind.Bool;
                } else if (word.SequenceEqual("null") || word.SequenceEqual("undefined")) {
                    kind = TokenKind.Null;
                } else if (ScanUtil.Contains(Keywords, word)) {
                    kind = TokenKind.Keyword;
                } else if (ScanUtil.Contains(Types, word)) {
                    kind = TokenKind.Type;
                } else {
                    int after = ScanUtil.NextNonSpace(line, end);
                    kind = after < line.Length && line[after] == ':' ? TokenKind.Key : TokenKind.Ident;
                }

                ScanUtil.Add(sink, i, end - i, kind);
                i = end;
                continue;
            }

            if (c is '{' or '}' or '[' or ']' or '(' or ')' or ';' or ',' or '.' or ':') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                continue;
            }

            ScanUtil.Add(sink, i, 1, TokenKind.Op);
            i++;
        }

        return Normal;
    }

    private static int TemplateEnd(ReadOnlySpan<char> line, int start) {
        int i = start;
        while (i < line.Length) {
            char c = line[i];
            if (c == '\\') {
                i += 2;
                continue;
            }

            i++;
            if (c == '`') return i;
        }

        return -1;
    }
}

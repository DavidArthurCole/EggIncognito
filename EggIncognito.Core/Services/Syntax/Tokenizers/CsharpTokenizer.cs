namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class CsharpTokenizer : ISyntaxTokenizer {
    private const byte Normal = 0;
    private const byte InBlockComment = 1;

    private static readonly string[] Keywords = [
        "abstract", "as", "async", "await", "base", "break", "case", "catch", "checked", "class",
        "const", "continue", "default", "delegate", "do", "else", "enum", "event", "explicit", "extern",
        "finally", "fixed", "for", "foreach", "get", "goto", "if", "implicit", "in", "init",
        "interface", "internal", "is", "lock", "namespace", "new", "operator", "out", "override", "params",
        "partial", "private", "protected", "public", "readonly", "record", "ref", "required", "return", "sealed",
        "set", "sizeof", "stackalloc", "static", "struct", "switch", "this", "throw", "try", "typeof",
        "unchecked", "unsafe", "using", "var", "virtual", "volatile", "when", "where", "while", "with",
        "yield", "global", "nameof"
    ];

    private static readonly string[] Types = [
        "bool", "byte", "char", "decimal", "double", "dynamic", "float", "int", "long", "nint",
        "nuint", "object", "sbyte", "short", "string", "uint", "ulong", "ushort", "void",
        "Task", "ValueTask", "List", "Dictionary", "HashSet", "IEnumerable", "IReadOnlyList", "Span", "Memory"
    ];

    public string Id => "csharp";

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

            if (c == '#' && i == ScanUtil.NextNonSpace(line, 0)) {
                ScanUtil.Add(sink, i, line.Length - i, TokenKind.Meta);
                return Normal;
            }

            if (c is '"' or '\'') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            if (c == '@' && i + 1 < line.Length && line[i + 1] == '"') {
                int close = ScanUtil.SkipQuoted(line, i + 1);
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

            if (c == '[' && i == ScanUtil.NextNonSpace(line, 0)) {
                ScanUtil.Add(sink, i, line.Length - i, TokenKind.Meta);
                return Normal;
            }

            if (ScanUtil.IsIdentStart(c)) {
                int end = ScanUtil.ReadIdent(line, i);
                var word = line[i..end];
                TokenKind kind;
                if (word.SequenceEqual("true") || word.SequenceEqual("false")) {
                    kind = TokenKind.Bool;
                } else if (word.SequenceEqual("null")) {
                    kind = TokenKind.Null;
                } else if (ScanUtil.Contains(Keywords, word)) {
                    kind = TokenKind.Keyword;
                } else if (ScanUtil.Contains(Types, word) || char.IsAsciiLetterUpper(c)) {
                    kind = TokenKind.Type;
                } else {
                    kind = TokenKind.Ident;
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
}

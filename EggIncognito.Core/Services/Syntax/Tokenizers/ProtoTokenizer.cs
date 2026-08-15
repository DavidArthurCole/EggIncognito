namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class ProtoTokenizer : ISyntaxTokenizer {
    private const byte Normal = 0;
    private const byte InBlockComment = 1;

    private static readonly string[] Keywords = [
        "syntax", "package", "import", "option", "message", "enum", "service", "rpc", "returns", "stream",
        "repeated", "optional", "required", "oneof", "map", "reserved", "extend", "extensions", "to", "max",
        "public", "weak", "group", "default", "deprecated", "packed"
    ];

    private static readonly string[] Types = [
        "double", "float", "int32", "int64", "uint32", "uint64", "sint32", "sint64",
        "fixed32", "fixed64", "sfixed32", "sfixed64", "bool", "string", "bytes"
    ];

    public string Id => "proto";

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

            if (c is '"' or '\'') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '-' && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1]))) {
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
                } else if (ScanUtil.Contains(Keywords, word)) {
                    kind = TokenKind.Keyword;
                } else if (ScanUtil.Contains(Types, word)) {
                    kind = TokenKind.Type;
                } else {
                    int after = ScanUtil.NextNonSpace(line, end);
                    kind = after < line.Length && line[after] == '=' ? TokenKind.Key : TokenKind.Ident;
                }

                ScanUtil.Add(sink, i, end - i, kind);
                i = end;
                continue;
            }

            if (c is '{' or '}' or '[' or ']' or '(' or ')' or ';' or ',' or '.' or '<' or '>') {
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

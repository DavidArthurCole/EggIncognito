namespace EggIncognito.Core.Services.Syntax.Tokenizers;

public sealed class SqlTokenizer : ISyntaxTokenizer {
    private const byte Normal = 0;
    private const byte InBlockComment = 1;

    private static readonly string[] Keywords = [
        "select", "from", "where", "insert", "into", "values", "update", "set", "delete", "create",
        "table", "index", "view", "drop", "alter", "add", "column", "primary", "key", "foreign",
        "references", "join", "inner", "left", "right", "full", "outer", "cross", "on", "group",
        "by", "order", "having", "limit", "offset", "distinct", "as", "and", "or", "not",
        "null", "is", "in", "between", "like", "exists", "case", "when", "then", "else",
        "end", "union", "all", "with", "returning", "conflict", "do", "nothing", "constraint", "default",
        "unique", "cascade", "begin", "commit", "rollback", "asc", "desc"
    ];

    private static readonly string[] Types = [
        "int", "integer", "bigint", "smallint", "serial", "bigserial", "text", "varchar", "char", "boolean",
        "bool", "date", "time", "timestamp", "timestamptz", "numeric", "decimal", "real", "double", "uuid",
        "json", "jsonb", "bytea"
    ];

    public string Id => "sql";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        Span<char> buffer = stackalloc char[32];
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

            if (c == '-' && i + 1 < line.Length && line[i + 1] == '-') {
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

            if (c == '\'') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            if (c is '"' or '`') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.Ident);
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
                TokenKind kind = TokenKind.Ident;
                if (word.Length <= buffer.Length) {
                    var lower = buffer[..word.Length];
                    word.ToLowerInvariant(lower);
                    if (lower.SequenceEqual("true") || lower.SequenceEqual("false")) kind = TokenKind.Bool;
                    else if (lower.SequenceEqual("null")) kind = TokenKind.Null;
                    else if (ScanUtil.Contains(Keywords, lower)) kind = TokenKind.Keyword;
                    else if (ScanUtil.Contains(Types, lower)) kind = TokenKind.Type;
                }

                ScanUtil.Add(sink, i, end - i, kind);
                i = end;
                continue;
            }

            if (c is '(' or ')' or ',' or ';' or '.') {
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

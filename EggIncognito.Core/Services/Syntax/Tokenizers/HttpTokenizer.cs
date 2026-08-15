namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class HttpTokenizer : ISyntaxTokenizer {
    private static readonly string[] Methods = [
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT"
    ];

    public string Id => "http";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        if (sink is null || line.Length == 0) return 0;

        int start = ScanUtil.NextNonSpace(line, 0);
        if (start >= line.Length) return 0;

        int wordEnd = start;
        while (wordEnd < line.Length && !char.IsWhiteSpace(line[wordEnd])) wordEnd++;
        var first = line[start..wordEnd];

        if (ScanUtil.Contains(Methods, first)) {
            ScanUtil.Add(sink, start, wordEnd - start, TokenKind.Keyword);
            int pathStart = ScanUtil.NextNonSpace(line, wordEnd);
            int pathEnd = pathStart;
            while (pathEnd < line.Length && !char.IsWhiteSpace(line[pathEnd])) pathEnd++;
            ScanUtil.Add(sink, pathStart, pathEnd - pathStart, TokenKind.String);
            int rest = ScanUtil.NextNonSpace(line, pathEnd);
            ScanUtil.Add(sink, rest, line.Length - rest, TokenKind.Meta);
            return 0;
        }

        if (first.StartsWith("HTTP/", StringComparison.Ordinal)) {
            ScanUtil.Add(sink, start, wordEnd - start, TokenKind.Meta);
            int codeStart = ScanUtil.NextNonSpace(line, wordEnd);
            int codeEnd = codeStart;
            while (codeEnd < line.Length && char.IsAsciiDigit(line[codeEnd])) codeEnd++;
            ScanUtil.Add(sink, codeStart, codeEnd - codeStart, TokenKind.Number);
            int rest = ScanUtil.NextNonSpace(line, codeEnd);
            ScanUtil.Add(sink, rest, line.Length - rest, TokenKind.Keyword);
            return 0;
        }

        int colon = line.IndexOf(':');
        if (colon > start) {
            ScanUtil.Add(sink, start, colon - start, TokenKind.Key);
            ScanUtil.Add(sink, colon, 1, TokenKind.Punct);
            int valueStart = ScanUtil.NextNonSpace(line, colon + 1);
            ScanUtil.Add(sink, valueStart, line.Length - valueStart, TokenKind.String);
            return 0;
        }

        ScanUtil.Add(sink, start, line.Length - start, TokenKind.Plain);
        return 0;
    }
}

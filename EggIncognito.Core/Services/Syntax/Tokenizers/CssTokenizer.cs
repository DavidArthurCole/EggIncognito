namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class CssTokenizer : ISyntaxTokenizer {
    private const byte Selector = 0;
    private const byte InBlock = 1;
    private const byte InComment = 2;

    public string Id => "css";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        byte mode = state is InBlock or InComment ? state : Selector;
        int i = 0;
        while (i < line.Length) {
            if (mode == InComment) {
                int close = line[i..].IndexOf("*/", StringComparison.Ordinal);
                if (close < 0) {
                    ScanUtil.Add(sink, i, line.Length - i, TokenKind.Comment);
                    return InComment;
                }

                ScanUtil.Add(sink, i, close + 2, TokenKind.Comment);
                i += close + 2;
                mode = Selector;
                continue;
            }

            char c = line[i];
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '*') {
                mode = InComment;
                continue;
            }

            if (c == '{') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                mode = InBlock;
                continue;
            }

            if (c == '}') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                mode = Selector;
                continue;
            }

            if (c is '"' or '\'') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            if (mode == Selector) {
                int end = i;
                while (end < line.Length && line[end] is not ('{' or '}' or ',' or ';')) end++;
                var kind = c == '@' ? TokenKind.Keyword : TokenKind.Tag;
                ScanUtil.Add(sink, i, TrimEnd(line, i, end) - i, kind);
                i = end;
                if (i < line.Length && line[i] is ',' or ';') {
                    ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                    i++;
                }

                continue;
            }

            if (c == ':') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                int valueEnd = i;
                while (valueEnd < line.Length && line[valueEnd] is not (';' or '}')) valueEnd++;
                ScanValue(line, ScanUtil.NextNonSpace(line, i), valueEnd, sink);
                i = valueEnd;
                continue;
            }

            if (c == ';') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                continue;
            }

            int propEnd = i;
            while (propEnd < line.Length && line[propEnd] is not (':' or ';' or '}')) propEnd++;
            ScanUtil.Add(sink, i, TrimEnd(line, i, propEnd) - i, TokenKind.Key);
            i = propEnd;
        }

        return mode;
    }

    private static void ScanValue(ReadOnlySpan<char> line, int start, int end, List<Token>? sink) {
        int i = start;
        while (i < end) {
            char c = line[i];
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c is '"' or '\'') {
                int close = Math.Min(ScanUtil.SkipQuoted(line, i), end);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '-' && i + 1 < end && char.IsAsciiDigit(line[i + 1])) || c == '#') {
                int numEnd = i + 1;
                while (numEnd < end && (char.IsAsciiLetterOrDigit(line[numEnd]) || line[numEnd] is '.' or '%')) numEnd++;
                ScanUtil.Add(sink, i, numEnd - i, TokenKind.Number);
                i = numEnd;
                continue;
            }

            if (ScanUtil.IsIdentStart(c) || c == '-') {
                int wordEnd = i;
                while (wordEnd < end && (ScanUtil.IsIdentPart(line[wordEnd]) || line[wordEnd] == '-')) wordEnd++;
                if (wordEnd == i) wordEnd++;
                ScanUtil.Add(sink, i, wordEnd - i, TokenKind.Ident);
                i = wordEnd;
                continue;
            }

            ScanUtil.Add(sink, i, 1, TokenKind.Punct);
            i++;
        }
    }

    private static int TrimEnd(ReadOnlySpan<char> line, int start, int end) {
        int e = end;
        while (e > start && char.IsWhiteSpace(line[e - 1])) e--;
        return e;
    }
}

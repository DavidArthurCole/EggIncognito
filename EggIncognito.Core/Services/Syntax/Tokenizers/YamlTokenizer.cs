namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class YamlTokenizer : ISyntaxTokenizer {
    private static readonly string[] BoolWords = ["true", "false", "yes", "no", "on", "off"];
    private static readonly string[] NullWords = ["null", "~"];

    public string Id => "yaml";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        if (sink is null) return 0;
        int i = ScanUtil.NextNonSpace(line, 0);
        if (i >= line.Length) return 0;

        if (line[i] == '#') {
            ScanUtil.Add(sink, i, line.Length - i, TokenKind.Comment);
            return 0;
        }

        if (line[i] == '-' && (i + 1 >= line.Length || line[i + 1] == ' ')) {
            ScanUtil.Add(sink, i, 1, TokenKind.Punct);
            i = ScanUtil.NextNonSpace(line, i + 1);
            if (i >= line.Length) return 0;
        }

        if (line[i] == '#') {
            ScanUtil.Add(sink, i, line.Length - i, TokenKind.Comment);
            return 0;
        }

        int colon = FindKeyColon(line, i);
        if (colon > i) {
            ScanUtil.Add(sink, i, colon - i, TokenKind.Key);
            ScanUtil.Add(sink, colon, 1, TokenKind.Punct);
            i = ScanUtil.NextNonSpace(line, colon + 1);
            if (i >= line.Length) return 0;
        }

        ScanValue(line, i, sink);
        return 0;
    }

    private static int FindKeyColon(ReadOnlySpan<char> line, int start) {
        int i = start;
        if (line[i] is '"' or '\'') {
            int end = ScanUtil.SkipQuoted(line, i);
            return end < line.Length && line[end] == ':' ? end : start;
        }

        while (i < line.Length) {
            char c = line[i];
            if (c == '#') return start;
            if (c == ':' && (i + 1 >= line.Length || line[i + 1] == ' ')) return i;
            i++;
        }

        return start;
    }

    private static void ScanValue(ReadOnlySpan<char> line, int start, List<Token> sink) {
        int end = line.Length;
        while (end > start && line[end - 1] == ' ') end--;
        int i = start;
        while (i < end) {
            char c = line[i];
            if (c == ' ') {
                i++;
                continue;
            }

            if (c is '[' or ']' or '{' or '}' or ',') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                continue;
            }

            if (c == '#') {
                ScanUtil.Add(sink, i, end - i, TokenKind.Comment);
                return;
            }

            if (c is '"' or '\'') {
                int close = Math.Min(ScanUtil.SkipQuoted(line, i), end);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            int stop = i;
            while (stop < end && line[stop] is not (' ' or ',' or ']' or '}')) stop++;
            var word = line[i..stop];
            var kind = c is '&' or '*' or '!' ? TokenKind.Meta
                : ScanUtil.Contains(BoolWords, word) ? TokenKind.Bool
                : ScanUtil.Contains(NullWords, word) ? TokenKind.Null
                : IsNumber(word) ? TokenKind.Number
                : TokenKind.Plain;
            ScanUtil.Add(sink, i, stop - i, kind);
            i = stop;
        }
    }

    private static bool IsNumber(ReadOnlySpan<char> word) {
        if (word.Length == 0) return false;
        int i = word[0] is '-' or '+' ? 1 : 0;
        if (i >= word.Length) return false;
        bool digit = false;
        for (; i < word.Length; i++) {
            char c = word[i];
            if (char.IsAsciiDigit(c)) {
                digit = true;
                continue;
            }

            if (c is '.' or 'e' or 'E' or '+' or '-') continue;
            return false;
        }

        return digit;
    }
}

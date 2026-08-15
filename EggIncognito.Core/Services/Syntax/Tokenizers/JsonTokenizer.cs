namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class JsonTokenizer : ISyntaxTokenizer {
    public string Id => "json";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        if (sink is null) return 0;
        int i = 0;
        while (i < line.Length) {
            char c = line[i];
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c == '"') {
                int start = i;
                i = ScanUtil.SkipQuoted(line, i);
                int after = ScanUtil.NextNonSpace(line, i);
                var kind = after < line.Length && line[after] == ':' ? TokenKind.Key : TokenKind.String;
                ScanUtil.Add(sink, start, i - start, kind);
                continue;
            }

            if (c == '-' || char.IsAsciiDigit(c)) {
                int start = i;
                i = ScanUtil.ReadNumber(line, i);
                if (i == start) i++;
                ScanUtil.Add(sink, start, i - start, TokenKind.Number);
                continue;
            }

            if (char.IsAsciiLetter(c)) {
                int start = i;
                while (i < line.Length && char.IsAsciiLetter(line[i])) i++;
                var word = line[start..i];
                var kind = word.SequenceEqual("true") || word.SequenceEqual("false") ? TokenKind.Bool
                    : word.SequenceEqual("null") ? TokenKind.Null
                    : TokenKind.Invalid;
                ScanUtil.Add(sink, start, i - start, kind);
                continue;
            }

            if (c is '{' or '}' or '[' or ']' or ',' or ':') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                continue;
            }

            ScanUtil.Add(sink, i, 1, TokenKind.Invalid);
            i++;
        }

        return 0;
    }
}

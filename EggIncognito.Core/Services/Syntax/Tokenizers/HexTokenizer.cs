namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class HexTokenizer : ISyntaxTokenizer {
    public string Id => "hex";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        if (sink is null) return 0;
        int i = 0;
        int offset = ScanUtil.OffsetColumn(line);
        if (offset > 0) {
            ScanUtil.Add(sink, 0, offset, TokenKind.Offset);
            i = offset + 2;
        }

        while (i < line.Length && line[i] != '|') {
            if (!char.IsAsciiHexDigit(line[i])) {
                i++;
                continue;
            }

            int start = i;
            while (i < line.Length && char.IsAsciiHexDigit(line[i])) i++;
            ScanUtil.Add(sink, start, i - start, TokenKind.Byte);
        }

        if (i >= line.Length) return 0;

        ScanUtil.Add(sink, i, 1, TokenKind.Punct);
        int closeRel = line[(i + 1)..].LastIndexOf('|');
        int asciiEnd = closeRel >= 0 ? i + 1 + closeRel : line.Length;
        ScanUtil.Add(sink, i + 1, asciiEnd - i - 1, TokenKind.Ascii);
        if (closeRel >= 0) ScanUtil.Add(sink, asciiEnd, 1, TokenKind.Punct);
        return 0;
    }
}

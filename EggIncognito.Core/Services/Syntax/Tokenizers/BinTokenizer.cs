namespace EggIncognito.Core.Services.Syntax.Tokenizers;

public sealed class BinTokenizer : ISyntaxTokenizer {
    public string Id => "bin";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        if (sink is null) return 0;
        int i = 0;
        int offset = ScanUtil.OffsetColumn(line);
        if (offset > 0) {
            ScanUtil.Add(sink, 0, offset, TokenKind.Offset);
            i = offset + 2;
        }

        while (i < line.Length) {
            char c = line[i];
            if (c is not ('0' or '1')) {
                i++;
                continue;
            }

            int start = i;
            while (i < line.Length && line[i] is '0' or '1') i++;
            ScanUtil.Add(sink, start, i - start, TokenKind.Byte);
        }

        return 0;
    }
}

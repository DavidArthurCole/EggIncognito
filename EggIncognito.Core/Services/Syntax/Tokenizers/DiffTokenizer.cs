namespace EggIncognito.Services.Syntax.Tokenizers;

public sealed class DiffTokenizer : ISyntaxTokenizer {
    public string Id => "diff";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        if (sink is null || line.Length == 0) return 0;

        if (line.StartsWith("@@", StringComparison.Ordinal)) {
            ScanUtil.Add(sink, 0, line.Length, TokenKind.Meta);
            return 0;
        }

        if (line.StartsWith("+++", StringComparison.Ordinal)
            || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal)
            || line.StartsWith("index ", StringComparison.Ordinal)) {
            ScanUtil.Add(sink, 0, line.Length, TokenKind.Meta);
            return 0;
        }

        if (line[0] == '\\') {
            ScanUtil.Add(sink, 0, line.Length, TokenKind.Comment);
            return 0;
        }

        if (line[0] is '+' or '-') {
            ScanUtil.Add(sink, 0, 1, TokenKind.Punct);
            ScanUtil.Add(sink, 1, line.Length - 1, TokenKind.Plain);
        }

        return 0;
    }
}

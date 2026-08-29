namespace EggIncognito.Core.Services.Syntax.Tokenizers;

public sealed class XmlTokenizer : ISyntaxTokenizer {
    private const byte Text = 0;
    private const byte InComment = 1;
    private const byte InTag = 2;

    public string Id => "xml";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        int i = 0;
        byte mode = state is InComment or InTag ? state : Text;
        while (i < line.Length) {
            if (mode == InComment) {
                int close = line[i..].IndexOf("-->", StringComparison.Ordinal);
                if (close < 0) {
                    ScanUtil.Add(sink, i, line.Length - i, TokenKind.Comment);
                    return InComment;
                }

                ScanUtil.Add(sink, i, close + 3, TokenKind.Comment);
                i += close + 3;
                mode = Text;
                continue;
            }

            if (mode == InTag) {
                i = ScanTagBody(line, i, sink, ref mode);
                continue;
            }

            int lt = line[i..].IndexOf('<');
            if (lt < 0) {
                ScanUtil.Add(sink, i, line.Length - i, TokenKind.Plain);
                return Text;
            }

            if (lt > 0) ScanUtil.Add(sink, i, lt, TokenKind.Plain);
            i += lt;

            if (line[i..].StartsWith("<!--", StringComparison.Ordinal)) {
                mode = InComment;
                continue;
            }

            if (line[i..].StartsWith("<?", StringComparison.Ordinal) || line[i..].StartsWith("<!", StringComparison.Ordinal)) {
                int close = line[i..].IndexOf('>');
                int len = close < 0 ? line.Length - i : close + 1;
                ScanUtil.Add(sink, i, len, TokenKind.Meta);
                i += len;
                continue;
            }

            ScanUtil.Add(sink, i, 1, TokenKind.Punct);
            i++;
            if (i < line.Length && line[i] == '/') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
            }

            int nameEnd = i;
            while (nameEnd < line.Length && (char.IsAsciiLetterOrDigit(line[nameEnd]) || line[nameEnd] is '_' or '-' or ':' or '.')) nameEnd++;
            ScanUtil.Add(sink, i, nameEnd - i, TokenKind.Tag);
            i = nameEnd;
            mode = InTag;
        }

        return mode;
    }

    private static int ScanTagBody(ReadOnlySpan<char> line, int start, List<Token>? sink, ref byte mode) {
        int i = start;
        while (i < line.Length) {
            char c = line[i];
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c == '>') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                mode = Text;
                return i + 1;
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '>') {
                ScanUtil.Add(sink, i, 2, TokenKind.Punct);
                mode = Text;
                return i + 2;
            }

            if (c == '=') {
                ScanUtil.Add(sink, i, 1, TokenKind.Op);
                i++;
                continue;
            }

            if (c is '"' or '\'') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                continue;
            }

            int end = i;
            while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] is not ('=' or '>' or '/' or '"' or '\'')) end++;
            if (end == i) end++;
            ScanUtil.Add(sink, i, end - i, TokenKind.Attr);
            i = end;
        }

        mode = InTag;
        return i;
    }
}

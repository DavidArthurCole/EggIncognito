namespace EggIncognito.Core.Services.Syntax.Tokenizers;

public sealed class MarkdownTokenizer : ISyntaxTokenizer {
    private const byte Normal = 0;
    private const byte InFence = 1;

    public string Id => "markdown";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        int start = ScanUtil.NextNonSpace(line, 0);
        bool fenceMarker = start < line.Length && line[start..].StartsWith("```", StringComparison.Ordinal);

        if (state == InFence) {
            ScanUtil.Add(sink, 0, line.Length, fenceMarker ? TokenKind.Meta : TokenKind.Plain);
            return fenceMarker ? Normal : InFence;
        }

        if (fenceMarker) {
            ScanUtil.Add(sink, 0, line.Length, TokenKind.Meta);
            return InFence;
        }

        if (start >= line.Length) return Normal;

        if (line[start] == '#') {
            ScanUtil.Add(sink, start, line.Length - start, TokenKind.Keyword);
            return Normal;
        }

        if (line[start] == '>') {
            ScanUtil.Add(sink, start, line.Length - start, TokenKind.Comment);
            return Normal;
        }

        if (line[start] is '-' or '*' or '+' && start + 1 < line.Length && line[start + 1] == ' ') {
            ScanUtil.Add(sink, start, 1, TokenKind.Punct);
            ScanInline(line, start + 1, sink);
            return Normal;
        }

        ScanInline(line, start, sink);
        return Normal;
    }

    private static void ScanInline(ReadOnlySpan<char> line, int start, List<Token>? sink) {
        int i = start;
        while (i < line.Length) {
            char c = line[i];
            if (c == '`') {
                int close = line[(i + 1)..].IndexOf('`');
                int end = close < 0 ? line.Length : i + close + 2;
                ScanUtil.Add(sink, i, end - i, TokenKind.String);
                i = end;
                continue;
            }

            if (c is '[' or ']' or '(' or ')' or '!') {
                ScanUtil.Add(sink, i, 1, TokenKind.Punct);
                i++;
                continue;
            }

            if (c is '*' or '_') {
                int run = i;
                while (run < line.Length && line[run] == c) run++;
                ScanUtil.Add(sink, i, run - i, TokenKind.Op);
                i = run;
                continue;
            }

            int plain = i;
            while (plain < line.Length && line[plain] is not ('`' or '[' or ']' or '(' or ')' or '!' or '*' or '_')) plain++;
            ScanUtil.Add(sink, i, plain - i, TokenKind.Plain);
            i = plain;
        }
    }
}

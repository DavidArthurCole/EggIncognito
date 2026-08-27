namespace EggIncognito.Services.Syntax.Tokenizers;

internal static class ScanUtil {
    public static int SkipQuoted(ReadOnlySpan<char> line, int start) {
        char quote = line[start];
        int i = start + 1;
        while (i < line.Length) {
            char c = line[i];
            if (c == '\\') {
                i += 2;
                continue;
            }

            i++;
            if (c == quote) return i;
        }

        return line.Length;
    }

    public static bool QuotedClosed(ReadOnlySpan<char> line, int start) {
        char quote = line[start];
        int i = start + 1;
        while (i < line.Length) {
            char c = line[i];
            if (c == '\\') {
                i += 2;
                continue;
            }

            i++;
            if (c == quote) return true;
        }

        return false;
    }

    public static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_' || c == '$';

    public static bool IsIdentPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '$';

    public static int ReadIdent(ReadOnlySpan<char> line, int start) {
        int i = start;
        while (i < line.Length && IsIdentPart(line[i])) i++;
        return i;
    }

    public static int ReadNumber(ReadOnlySpan<char> line, int start) {
        int i = start;
        if (i < line.Length && (line[i] == '-' || line[i] == '+')) i++;
        if (i + 1 < line.Length && line[i] == '0' && (line[i + 1] == 'x' || line[i + 1] == 'X')) {
            i += 2;
            while (i < line.Length && (char.IsAsciiHexDigit(line[i]) || line[i] == '_')) i++;
            return i;
        }

        while (i < line.Length && (char.IsAsciiDigit(line[i]) || line[i] == '_')) i++;
        if (i < line.Length && line[i] == '.') {
            i++;
            while (i < line.Length && char.IsAsciiDigit(line[i])) i++;
        }

        if (i < line.Length && (line[i] == 'e' || line[i] == 'E')) {
            int save = i;
            i++;
            if (i < line.Length && (line[i] == '+' || line[i] == '-')) i++;
            if (i < line.Length && char.IsAsciiDigit(line[i])) {
                while (i < line.Length && char.IsAsciiDigit(line[i])) i++;
            } else {
                i = save;
            }
        }

        while (i < line.Length && line[i] is 'f' or 'd' or 'm' or 'L' or 'u' or 'U' or 'l') i++;
        return i;
    }

    public static int NextNonSpace(ReadOnlySpan<char> line, int start) {
        int i = start;
        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
        return i;
    }

    public static bool IsHexRun(ReadOnlySpan<char> line, int start, int count) {
        if (start + count > line.Length) return false;
        for (int i = 0; i < count; i++) {
            if (!char.IsAsciiHexDigit(line[start + i])) return false;
        }

        return true;
    }

    public static int OffsetColumn(ReadOnlySpan<char> line) => line.Length >= 10 && IsHexRun(line, 0, 8) && line[8] == ' ' && line[9] == ' ' ? 8 : 0;

    public static void Add(List<Token>? sink, int start, int length, TokenKind kind) {
        if (sink is null || length <= 0) return;
        sink.Add(new Token(start, length, kind));
    }

    public static bool Contains(string[] words, ReadOnlySpan<char> word) {
        foreach (string w in words) {
            if (word.SequenceEqual(w)) return true;
        }

        return false;
    }
}

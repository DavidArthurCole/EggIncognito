namespace EggIncognito.Core.Services.Syntax.Tokenizers;

public sealed class BashTokenizer : ISyntaxTokenizer {
    private static readonly string[] Keywords = [
        "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done",
        "case", "esac", "in", "function", "select", "time", "return", "break", "continue", "local",
        "export", "readonly", "declare", "unset", "source", "alias", "set"
    ];

    private static readonly string[] Builtins = [
        "cd", "echo", "printf", "read", "test", "exit", "eval", "exec", "trap", "kill",
        "ls", "cat", "grep", "sed", "awk", "curl", "git", "docker", "dotnet", "npm", "sudo", "chmod", "mkdir", "rm", "cp", "mv"
    ];

    public string Id => "bash";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) {
        int i = 0;
        bool first = true;
        while (i < line.Length) {
            char c = line[i];
            if (char.IsWhiteSpace(c)) {
                i++;
                continue;
            }

            if (c == '#') {
                ScanUtil.Add(sink, i, line.Length - i, TokenKind.Comment);
                return 0;
            }

            if (c is '"' or '\'') {
                int close = ScanUtil.SkipQuoted(line, i);
                ScanUtil.Add(sink, i, close - i, TokenKind.String);
                i = close;
                first = false;
                continue;
            }

            if (c == '$') {
                int end = i + 1;
                if (end < line.Length && line[end] == '{') {
                    while (end < line.Length && line[end] != '}') end++;
                    if (end < line.Length) end++;
                } else {
                    end = ScanUtil.ReadIdent(line, end);
                    if (end == i + 1) end = Math.Min(i + 2, line.Length);
                }

                ScanUtil.Add(sink, i, end - i, TokenKind.Meta);
                i = end;
                first = false;
                continue;
            }

            if (c == '-' && i + 1 < line.Length && !char.IsWhiteSpace(line[i + 1])) {
                int end = i + 1;
                while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] is not ('=' or '"' or '\'')) end++;
                ScanUtil.Add(sink, i, end - i, TokenKind.Attr);
                i = end;
                first = false;
                continue;
            }

            if (char.IsAsciiDigit(c)) {
                int end = ScanUtil.ReadNumber(line, i);
                if (end == i) end++;
                ScanUtil.Add(sink, i, end - i, TokenKind.Number);
                i = end;
                first = false;
                continue;
            }

            if (ScanUtil.IsIdentStart(c)) {
                int end = ScanUtil.ReadIdent(line, i);
                var word = line[i..end];
                TokenKind kind = ScanUtil.Contains(Keywords, word) ? TokenKind.Keyword
                    : first && ScanUtil.Contains(Builtins, word) ? TokenKind.Type
                    : end < line.Length && line[end] == '=' ? TokenKind.Key
                    : TokenKind.Ident;
                ScanUtil.Add(sink, i, end - i, kind);
                i = end;
                first = false;
                continue;
            }

            if (c is '|' or '&' or ';' or '<' or '>') {
                ScanUtil.Add(sink, i, 1, TokenKind.Op);
                i++;
                first = true;
                continue;
            }

            ScanUtil.Add(sink, i, 1, TokenKind.Punct);
            i++;
            first = false;
        }

        return 0;
    }
}

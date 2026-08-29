namespace EggIncognito.Core.Services.Syntax;

public readonly record struct Token(int Start, int Length, TokenKind Kind);

public interface ISyntaxTokenizer {
    string Id { get; }

    byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink);
}

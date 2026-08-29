namespace EggIncognito.Core.Services.Syntax.Tokenizers;

public sealed class TextTokenizer : ISyntaxTokenizer {
    public string Id => "text";

    public byte Scan(ReadOnlySpan<char> line, byte state, List<Token>? sink) => 0;
}

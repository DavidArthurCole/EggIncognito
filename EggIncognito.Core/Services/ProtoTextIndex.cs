using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services;

public static partial class ProtoTextIndex {
    [GeneratedRegex(@"^\s*(?:message|enum)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex DeclRegex();

    public static IReadOnlyList<string> Names(string protoText) =>
        DeclRegex().Matches(protoText ?? "").Select(m => m.Groups[1].Value).Distinct().ToList();
}

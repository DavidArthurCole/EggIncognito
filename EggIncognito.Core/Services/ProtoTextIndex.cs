using System.Text.RegularExpressions;

namespace EggIncognito.Services;

// Pulls top-level message + enum names from .proto text for the registry's searchable index. Not a
// full parser; a name list is all the index needs.
public static partial class ProtoTextIndex
{
    [GeneratedRegex(@"^\s*(?:message|enum)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex DeclRegex();

    public static IReadOnlyList<string> Names(string protoText) =>
        DeclRegex().Matches(protoText ?? "").Select(m => m.Groups[1].Value).Distinct().ToList();
}

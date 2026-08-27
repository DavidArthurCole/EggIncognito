using System.Text.RegularExpressions;

namespace EggIncognito.CssBuild;

public static partial class ContentScanner {
    [GeneratedRegex(@"!?-?[a-zA-Z0-9_][a-zA-Z0-9_:/.\[\]%!-]*")]
    private static partial Regex TokenPattern();

    public static HashSet<string> Scan(IEnumerable<string> filePaths) {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in filePaths) {
            var text = File.ReadAllText(path);
            foreach (Match match in TokenPattern().Matches(text)) {
                var token = match.Value;
                if (token.Length < 2) continue;
                candidates.Add(token);
            }
        }
        return candidates;
    }
}

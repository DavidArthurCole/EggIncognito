namespace EggIncognito.Services.ProtoExtract;

public static class ProtoCleanup {
    public static string Clean(string eiProto, string commonProto) {
        var commonLines = SplitKeepEnds(commonProto);
        commonLines = [.. commonLines.Skip(3)];
        if (commonLines.Count > 0)
            commonLines[^1] = commonLines[^1].TrimEnd();

        var lines = SplitKeepEnds(eiProto);


        lines = [.. lines.Where(l => !l.TrimStart().StartsWith("import \"common.proto\";", StringComparison.Ordinal))];

        var packageIndex = lines.FindIndex(l => l.TrimStart().StartsWith("package ei;", StringComparison.Ordinal));
        if (packageIndex >= 0 && commonLines.Count > 0)
            lines.InsertRange(packageIndex + 1, commonLines);

        lines = [.. lines.Select(l => l.Replace("aux.", ""))];

        return string.Concat(lines);
    }


    private static List<string> SplitKeepEnds(string text) {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var result = new List<string>();
        int start = 0;
        for (int i = 0; i < normalized.Length; i++) {
            if (normalized[i] == '\n') {
                result.Add(normalized.Substring(start, i - start + 1));
                start = i + 1;
            }
        }
        if (start < normalized.Length)
            result.Add(normalized[start..]);
        return result;
    }
}

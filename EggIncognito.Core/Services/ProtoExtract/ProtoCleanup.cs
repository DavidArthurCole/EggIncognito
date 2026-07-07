namespace EggIncognito.Services.ProtoExtract;

// Port of protocleanup.py. Merges common.proto's body into ei.proto after the `package ei;` line,
// drops the `import "common.proto";` line, and strips `aux.` prefixes. Pure: same text in, same text
// out, line-for-line with the python so the farm's protoSha parity holds. Line endings normalized to
// \n (the python opens text-mode; the canonical bytes are LF).
public static class ProtoCleanup
{
    public static string Clean(string eiProto, string commonProto)
    {
        // python readlines() keeps the trailing \n on each line; we mirror that with a keep-ends split.
        var commonLines = SplitKeepEnds(commonProto);
        // Skip the first 3 lines (syntax, package, blank).
        commonLines = commonLines.Skip(3).ToList();
        // rstrip the last remaining common line's trailing whitespace/newline.
        if (commonLines.Count > 0)
            commonLines[^1] = commonLines[^1].TrimEnd();

        var lines = SplitKeepEnds(eiProto);

        // Drop the import line (matched on the trimmed text).
        lines = lines.Where(l => !l.TrimStart().StartsWith("import \"common.proto\";", StringComparison.Ordinal)).ToList();

        var packageIndex = lines.FindIndex(l => l.TrimStart().StartsWith("package ei;", StringComparison.Ordinal));
        if (packageIndex >= 0 && commonLines.Count > 0)
            lines.InsertRange(packageIndex + 1, commonLines);

        lines = lines.Select(l => l.Replace("aux.", "")).ToList();

        return string.Concat(lines);
    }

    // Splits text into lines preserving the trailing \n on each, matching python's readlines() over a
    // file opened in text mode (CRLF already normalized to LF by the read). A final line without a
    // newline keeps no newline, just like python.
    private static List<string> SplitKeepEnds(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var result = new List<string>();
        int start = 0;
        for (int i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] == '\n')
            {
                result.Add(normalized.Substring(start, i - start + 1));
                start = i + 1;
            }
        }
        if (start < normalized.Length)
            result.Add(normalized.Substring(start));
        return result;
    }
}

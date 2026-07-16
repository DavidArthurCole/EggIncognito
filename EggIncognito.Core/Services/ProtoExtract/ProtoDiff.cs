using System.Text.RegularExpressions;

namespace EggIncognito.Services.ProtoExtract;


public static partial class ProtoDiff
{
    [GeneratedRegex(@"\b(ei|aux)\.")]
    private static partial Regex NamespaceRe();

    [GeneratedRegex(@"^message\s+(\w+)\s*\{")]
    private static partial Regex MessageOpenRe();

    [GeneratedRegex(@"^(enum|oneof|extend|service)\s+(\w+)\s*\{")]
    private static partial Regex OtherOpenRe();

    private static string Normalize(string line) => NamespaceRe().Replace(line, "");

    public static string Diff(string oldProto, string newProto)
    {
        var old = Parse(oldProto);
        var @new = Parse(newProto);

       
        var seen = new HashSet<string>();
        var paths = new List<string>();
        foreach (var p in @new.Keys) { paths.Add(p); seen.Add(p); }
        foreach (var p in old.Keys) if (!seen.Contains(p)) paths.Add(p);

        var sections = new List<string>();

        foreach (var path in paths)
        {
            var oldLines = old.TryGetValue(path, out var ol) ? ol : [];
            var newLines = @new.TryGetValue(path, out var nl) ? nl : [];

            var oldNorm = oldLines.Select(Normalize).ToList();
            var newNorm = newLines.Select(Normalize).ToList();

            if (oldNorm.SequenceEqual(newNorm)) continue;

            var diffLines = new List<string>();
            foreach (var (tag, i1, i2, j1, j2) in GetOpcodes(oldNorm, newNorm))
            {
                switch (tag)
                {
                    case "equal":
                        break;
                    case "insert":
                        for (var j = j1; j < j2; j++) diffLines.Add("+" + newLines[j].TrimEnd('\n'));
                        break;
                    case "delete":
                        for (var i = i1; i < i2; i++) diffLines.Add("-" + oldLines[i].TrimEnd('\n'));
                        break;
                    case "replace":
                        for (var i = i1; i < i2; i++) diffLines.Add("-" + oldLines[i].TrimEnd('\n'));
                        for (var j = j1; j < j2; j++) diffLines.Add("+" + newLines[j].TrimEnd('\n'));
                        break;
                }
            }

            if (diffLines.Count > 0)
            {
                sections.Add($"@@ {path} @@");
                sections.AddRange(diffLines);
                sections.Add("");
            }
        }

        return string.Join("\n", sections);
    }

   
   
    private static Dictionary<string, List<string>> Parse(string protoText)
    {
        var messages = new Dictionary<string, List<string>>();
        var stack = new List<(string Kind, string Name, int Depth)>();
        var depth = 0;

        foreach (var line in SplitKeepEnds(protoText))
        {
            var s = line.Trim();

            var m = MessageOpenRe().Match(s);
            if (m.Success)
            {
                stack.Add(("message", m.Groups[1].Value, depth));
                depth += Count(s, '{') - Count(s, '}');
                continue;
            }

            m = OtherOpenRe().Match(s);
            if (m.Success)
            {
                var p = Path(stack);
                if (p is not null) Add(messages, p, line);
                stack.Add(("other", m.Groups[2].Value, depth));
                depth += Count(s, '{') - Count(s, '}');
                continue;
            }

            if (s == "}")
            {
                depth -= 1;
                if (stack.Count > 0 && stack[^1].Depth == depth)
                {
                    var (kind, _, _) = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    if (kind == "other")
                    {
                        var p = Path(stack);
                        if (p is not null) Add(messages, p, line);
                    }
                }
                continue;
            }

            depth += Count(s, '{') - Count(s, '}');

            var path = Path(stack);
            if (path is not null) Add(messages, path, line);
        }

        return messages;
    }

    private static void Add(Dictionary<string, List<string>> messages, string path, string line)
    {
        if (!messages.TryGetValue(path, out var list))
        {
            list = [];
            messages[path] = list;
        }
        list.Add(line);
    }

    private static string? Path(List<(string Kind, string Name, int Depth)> stack)
    {
        var parts = stack.Where(e => e.Kind == "message").Select(e => e.Name).ToList();
        if (parts.Count == 0) return null;
        if (parts.Count == 1) return $"message {parts[0]}";
        return $"message ({string.Join(".", parts.Take(parts.Count - 1))}.){parts[^1]}";
    }

    private static int Count(string s, char c)
    {
        var n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }

    private static List<string> SplitKeepEnds(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var result = new List<string>();
        var start = 0;
        for (var i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] == '\n')
            {
                result.Add(normalized.Substring(start, i - start + 1));
                start = i + 1;
            }
        }
        if (start < normalized.Length) result.Add(normalized.Substring(start));
        return result;
    }

   
   
    private static List<(string Tag, int I1, int I2, int J1, int J2)> GetOpcodes(
        IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var matches = LongestCommonSubsequence(a, b);
       
        matches.Add((a.Count, b.Count, 0));

        var opcodes = new List<(string, int, int, int, int)>();
        int i = 0, j = 0;
        foreach (var (ai, bj, size) in matches)
        {
            var tag = "";
            if (i < ai && j < bj) tag = "replace";
            else if (i < ai) tag = "delete";
            else if (j < bj) tag = "insert";
            if (tag.Length > 0) opcodes.Add((tag, i, ai, j, bj));
            i = ai + size;
            j = bj + size;
            if (size > 0) opcodes.Add(("equal", ai, i, bj, j));
        }
        return opcodes;
    }

   
    private static List<(int A, int B, int Size)> LongestCommonSubsequence(
        IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var pairs = new List<(int A, int B)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { pairs.Add((x, y)); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
            else y++;
        }

       
        var blocks = new List<(int A, int B, int Size)>();
        var k = 0;
        while (k < pairs.Count)
        {
            var startA = pairs[k].A;
            var startB = pairs[k].B;
            var size = 1;
            while (k + 1 < pairs.Count && pairs[k + 1].A == pairs[k].A + 1 && pairs[k + 1].B == pairs[k].B + 1)
            {
                size++;
                k++;
            }
            blocks.Add((startA, startB, size));
            k++;
        }
        return blocks;
    }
}

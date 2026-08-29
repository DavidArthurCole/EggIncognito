using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class UnifiedDiffWriterTests {
    private static readonly UnifiedDiffOptions Opts = new();

    private static string Join(params string[] lines) => string.Join("\n", lines);

    private static IReadOnlyList<string> Apply(IReadOnlyList<string> a, string patch) {
        var result = new List<string>();
        var patchLines = patch.Split('\n');
        int ai = 0, i = 0;
        while (i < patchLines.Length) {
            string line = patchLines[i];
            if (line.StartsWith("---") || line.StartsWith("+++")) { i++; continue; }
            if (line.StartsWith("@@")) {
                string spec = line.Split(' ')[1];
                int start = int.Parse(spec.TrimStart('-').Split(',')[0]);
                while (ai < start - 1) result.Add(a[ai++]);
                i++;
                continue;
            }
            if (line.StartsWith('\\')) { i++; continue; }
            if (line.StartsWith('+')) {
                result.Add(line[1..]);
            } else if (line.StartsWith('-')) {
                ai++;
            } else if (line.StartsWith(' ')) {
                result.Add(a[ai]);
                ai++;
            }
            i++;
        }
        while (ai < a.Count) result.Add(a[ai++]);
        return result;
    }

    [Fact]
    public void IdenticalInputsProduceEmptyOutput() => Assert.Equal("", UnifiedDiffWriter.Write("same\ntext", "same\ntext", Opts));

    [Fact]
    public void HeaderCarriesPathsAndLabels() {
        string patch = UnifiedDiffWriter.Write("a", "b", Opts with { LabelA = "ios 1.36", LabelB = "ios 1.37" });
        var lines = patch.Split('\n');
        Assert.Equal("--- a/ei.proto\tios 1.36", lines[0]);
        Assert.Equal("+++ b/ei.proto\tios 1.37", lines[1]);
    }

    [Fact]
    public void SingleLineChangeHunkMathIsCorrect() {
        string aText = Join("l1", "l2", "l3", "l4", "OLD", "l6", "l7", "l8", "l9");
        string bText = Join("l1", "l2", "l3", "l4", "NEW", "l6", "l7", "l8", "l9");
        string patch = UnifiedDiffWriter.Write(aText, bText, Opts);
        Assert.Contains("@@ -2,7 +2,7 @@", patch);
        Assert.Contains("-OLD", patch);
        Assert.Contains("+NEW", patch);
    }

    [Fact]
    public void FarApartChangesProduceTwoHunks() {
        var a = Enumerable.Range(1, 40).Select(i => "l" + i).ToArray();
        var b = a.ToArray();
        b[1] = "changed-early";
        b[35] = "changed-late";
        string patch = UnifiedDiffWriter.Write(string.Join("\n", a), string.Join("\n", b), Opts);
        Assert.Equal(2, patch.Split('\n').Count(l => l.StartsWith("@@")));
    }

    [Fact]
    public void NearbyChangesMergeIntoOneHunk() {
        var a = Enumerable.Range(1, 40).Select(i => "l" + i).ToArray();
        var b = a.ToArray();
        b[10] = "x";
        b[13] = "y";
        string patch = UnifiedDiffWriter.Write(string.Join("\n", a), string.Join("\n", b), Opts);
        Assert.Equal(1, patch.Split('\n').Count(l => l.StartsWith("@@")));
    }

    [Fact]
    public void FirstAndLastLineChangesAreClamped() {
        string patch = UnifiedDiffWriter.Write(Join("a", "b", "c"), Join("A", "b", "C"), Opts);
        Assert.Contains("@@ -1,3 +1,3 @@", patch);
    }

    [Fact]
    public void PureInsertionUsesZeroCountOnTheEmptySide() {
        string patch = UnifiedDiffWriter.Write("", "only", Opts);
        Assert.Contains("@@ -0,0 +1 @@", patch);
    }

    [Fact]
    public void MissingTrailingNewlineIsMarked() {
        string patch = UnifiedDiffWriter.Write("a\nb", "a\nc\n", Opts);
        Assert.Contains("\\ No newline at end of file", patch);
    }

    [Fact]
    public void PatchAppliesToProduceB() {
        var a = Enumerable.Range(1, 60).Select(i => "line" + i).ToArray();
        var b = a.ToArray();
        b[3] = "edited";
        b[44] = "edited-too";
        string patch = UnifiedDiffWriter.Write(string.Join("\n", a), string.Join("\n", b), Opts);
        Assert.Equal(b, Apply(a, patch).ToArray());
    }

    [Fact]
    public void SplitLinesStripsCarriageReturns() => Assert.Equal(new[] { "a", "b" }, UnifiedDiffWriter.SplitLines("a\r\nb"));
}

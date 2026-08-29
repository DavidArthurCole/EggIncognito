using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class SideBySideDiffBuilderTests {
    [Fact]
    public void IdenticalTextIsAllContextRows() {
        var result = SideBySideDiffBuilder.Build("a\nb", "a\nb");
        Assert.All(result.Rows, r => Assert.Equal(DiffRowKind.Context, r.Kind));
        Assert.Empty(result.HunkStarts);
    }

    [Fact]
    public void ContextRowsCarryBothSides() {
        var result = SideBySideDiffBuilder.Build("a\nb", "a\nb");
        Assert.All(result.Rows, r => {
            Assert.NotNull(r.LeftNo);
            Assert.NotNull(r.RightNo);
            Assert.NotNull(r.Left);
            Assert.NotNull(r.Right);
        });
    }

    [Fact]
    public void LineNumbersAreMonotonicPerSide() {
        var result = SideBySideDiffBuilder.Build("a\nb\nc\nd", "a\nx\nc\nd\ne");
        int lastLeft = 0, lastRight = 0;
        foreach (var row in result.Rows) {
            if (row.LeftNo is { } l) { Assert.True(l > lastLeft); lastLeft = l; }
            if (row.RightNo is { } r) { Assert.True(r > lastRight); lastRight = r; }
        }
    }

    [Fact]
    public void DeleteInsertPairBecomesChangedRow() {
        var result = SideBySideDiffBuilder.Build("a\nold\nz", "a\nnew\nz");
        var changed = Assert.Single(result.Rows, r => r.Kind == DiffRowKind.Changed);
        Assert.Equal("old", changed.Left);
        Assert.Equal("new", changed.Right);
    }

    [Fact]
    public void LongerInsertRunSpillsIntoAddedRows() {
        var result = SideBySideDiffBuilder.Build("a\nold\nz", "a\nnew1\nnew2\nz");
        Assert.Single(result.Rows, r => r.Kind == DiffRowKind.Changed);
        var added = Assert.Single(result.Rows, r => r.Kind == DiffRowKind.Added);
        Assert.Null(added.Left);
        Assert.Null(added.LeftNo);
        Assert.Equal("new2", added.Right);
    }

    [Fact]
    public void LongerDeleteRunSpillsIntoRemovedRows() {
        var result = SideBySideDiffBuilder.Build("a\nold1\nold2\nz", "a\nnew\nz");
        var removed = Assert.Single(result.Rows, r => r.Kind == DiffRowKind.Removed);
        Assert.Null(removed.Right);
        Assert.Null(removed.RightNo);
        Assert.Equal("old2", removed.Left);
    }

    [Fact]
    public void ChangedRowGetsWordLevelInk() {
        var result = SideBySideDiffBuilder.Build("optional int32 id = 1;", "optional int64 id = 1;");
        var changed = Assert.Single(result.Rows, r => r.Kind == DiffRowKind.Changed);
        Assert.NotEmpty(changed.LeftInk);
        Assert.NotEmpty(changed.RightInk);
        var span = changed.LeftInk[0];
        Assert.Equal("int32", changed.Left!.Substring(span.Start, span.Length));
    }

    [Fact]
    public void InkIsSkippedForVeryLongLines() {
        string longA = new('a', 500);
        string longB = new('b', 500);
        var result = SideBySideDiffBuilder.Build(longA, longB);
        var changed = Assert.Single(result.Rows, r => r.Kind == DiffRowKind.Changed);
        Assert.Empty(changed.LeftInk);
        Assert.Empty(changed.RightInk);
    }

    [Fact]
    public void HunkStartsPointAtChangeRegionStarts() {
        var a = string.Join("\n", Enumerable.Range(1, 20).Select(i => "l" + i));
        var lines = a.Split('\n').ToArray();
        lines[5] = "x";
        lines[15] = "y";
        var result = SideBySideDiffBuilder.Build(a, string.Join("\n", lines));
        Assert.Equal(2, result.HunkStarts.Count);
        Assert.All(result.HunkStarts, i => Assert.NotEqual(DiffRowKind.Context, result.Rows[i].Kind));
    }

    [Fact]
    public void TokenizeSplitsIdentifiersFromPunctuation() => Assert.Equal(new[] { "ei.Foo", " ", "=", " ", "1", ";" }, SideBySideDiffBuilder.Tokenize("ei.Foo = 1;"));
}

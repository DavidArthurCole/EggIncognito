using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class MyersDiffTests {
    private static IReadOnlyList<string> L(params string[] v) => v;

    [Fact]
    public void EmptyInputsProduceNoOps() => Assert.Empty(MyersDiff.Compute(L(), L()));

    [Fact]
    public void IdenticalInputsProduceSingleEqual() {
        var ops = MyersDiff.Compute(L("a", "b", "c"), L("a", "b", "c"));
        var op = Assert.Single(ops);
        Assert.Equal(DiffOpKind.Equal, op.Kind);
        Assert.Equal(3, op.ALength);
        Assert.Equal(3, op.BLength);
    }

    [Fact]
    public void AllInsertsWhenAEmpty() {
        var ops = MyersDiff.Compute(L(), L("x", "y"));
        var op = Assert.Single(ops);
        Assert.Equal(DiffOpKind.Insert, op.Kind);
        Assert.Equal(0, op.ALength);
        Assert.Equal(2, op.BLength);
    }

    [Fact]
    public void AllDeletesWhenBEmpty() {
        var ops = MyersDiff.Compute(L("x", "y"), L());
        var op = Assert.Single(ops);
        Assert.Equal(DiffOpKind.Delete, op.Kind);
        Assert.Equal(2, op.ALength);
        Assert.Equal(0, op.BLength);
    }

    [Fact]
    public void ReplacementEmitsDeleteBeforeInsert() {
        var ops = MyersDiff.Compute(L("a", "old", "z"), L("a", "new", "z"));
        Assert.Collection(ops,
            o => Assert.Equal(DiffOpKind.Equal, o.Kind),
            o => Assert.Equal(DiffOpKind.Delete, o.Kind),
            o => Assert.Equal(DiffOpKind.Insert, o.Kind),
            o => Assert.Equal(DiffOpKind.Equal, o.Kind));
    }

    [Fact]
    public void OpsAreCoalescedAndCoverBothSequences() {
        var a = L("1", "2", "3", "4", "5");
        var b = L("1", "9", "9", "4", "5", "6");
        var ops = MyersDiff.Compute(a, b);
        for (int i = 1; i < ops.Count; i++) Assert.NotEqual(ops[i - 1].Kind, ops[i].Kind);
        Assert.Equal(a.Count, ops.Where(o => o.Kind != DiffOpKind.Insert).Sum(o => o.ALength));
        Assert.Equal(b.Count, ops.Where(o => o.Kind != DiffOpKind.Delete).Sum(o => o.BLength));
    }

    [Fact]
    public void OpsAreContiguousInBothSequences() {
        var ops = MyersDiff.Compute(L("a", "b", "c", "d"), L("a", "x", "c", "d", "e"));
        int ai = 0, bi = 0;
        foreach (var op in ops) {
            Assert.Equal(ai, op.AStart);
            Assert.Equal(bi, op.BStart);
            ai += op.ALength;
            bi += op.BLength;
        }
    }

    [Fact]
    public void GuardFallsBackToCoarseDiffOnHugeInput() {
        var a = Enumerable.Range(0, 210_000).Select(i => "a" + i).ToArray();
        var b = Enumerable.Range(0, 210_000).Select(i => "b" + i).ToArray();
        var ops = MyersDiff.Compute(a, b);
        Assert.Collection(ops,
            o => Assert.Equal(DiffOpKind.Delete, o.Kind),
            o => Assert.Equal(DiffOpKind.Insert, o.Kind));
    }

    [Fact]
    public void ComparerIsHonored() {
        var ops = MyersDiff.Compute(L("A", "B"), L("a", "b"), StringComparer.OrdinalIgnoreCase);
        Assert.Single(ops);
        Assert.Equal(DiffOpKind.Equal, ops[0].Kind);
    }
}

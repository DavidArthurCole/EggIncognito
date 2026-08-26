using EggIncognito.Services.ProtoExtract;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class ShaLanesTests {
    private static VersionKey Key(string? sha, string platform = "ios") =>
        new(platform, "1.0.0", "100", null, null, null, null, sha);

    private static IReadOnlyDictionary<long, ShaLaneRow> Build(params string?[] shas) {
        var visible = shas.Select(s => Key(s)).ToList();
        var keys = Enumerable.Range(0, shas.Length).Select(i => (long)i).ToList();
        return ShaLanes.Build(keys, visible, visible);
    }

    private static ShaLaneMark MarkAt(IReadOnlyDictionary<long, ShaLaneRow> rows, long key, int lane) =>
        rows[key].Segments.FirstOrDefault(s => s.Lane == lane)?.Mark ?? ShaLaneMark.None;

    [Fact]
    public void AdjacentPair_DrawsStartThenEndOnLaneZero() {
        var rows = Build("aa", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(rows, 0, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(rows, 1, 0));
        Assert.Equal(1, ShaLanes.LaneCount(rows));
    }

    [Fact]
    public void NonAdjacentPair_PassesThroughTheRowsBetween() {
        var rows = Build("aa", "bb", "cc", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(rows, 0, 0));
        Assert.Equal(ShaLaneMark.Pass, MarkAt(rows, 1, 0));
        Assert.Equal(ShaLaneMark.Pass, MarkAt(rows, 2, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(rows, 3, 0));
    }

    [Fact]
    public void ThreeMembers_MarkTheMiddleAsNode() {
        var rows = Build("aa", "aa", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(rows, 0, 0));
        Assert.Equal(ShaLaneMark.Node, MarkAt(rows, 1, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(rows, 2, 0));
    }

    [Fact]
    public void OverlappingGroups_TakeSeparateLanes() {
        var rows = Build("aa", "bb", "aa", "bb");

        Assert.Equal(0, rows[0].Segments.Single().Lane);
        Assert.Equal(1, rows[1].Segments.Single(s => s.Mark == ShaLaneMark.Start).Lane);
        Assert.Equal(2, ShaLanes.LaneCount(rows));
    }

    [Fact]
    public void DisjointGroups_ShareOneLane() {
        var rows = Build("aa", "aa", "bb", "bb");

        Assert.Equal(0, rows[2].Segments.Single().Lane);
        Assert.Equal(1, ShaLanes.LaneCount(rows));
    }

    [Fact]
    public void FiveOverlappingGroups_StopAtTheLaneCap() {
        var rows = Build("aa", "bb", "cc", "dd", "ee", "aa", "bb", "cc", "dd", "ee");

        Assert.Equal(4, ShaLanes.LaneCount(rows));
        Assert.All(rows[4].Segments, s => Assert.Equal(ShaLaneMark.Pass, s.Mark));
        Assert.Empty(rows[9].Segments);
        Assert.Equal(2, rows[4].VisibleGroupSize);
    }

    [Fact]
    public void GroupReducedToOneByFiltering_DrawsNothingButKeepsTheHiddenCount() {
        var all = new List<VersionKey> { Key("aa"), Key("aa"), Key("bb") };
        var visible = new List<VersionKey> { all[0], all[2] };
        var rows = ShaLanes.Build([0L, 2L], visible, all);

        Assert.Empty(rows[0].Segments);
        Assert.Equal(2, rows[0].GroupSize);
        Assert.Equal(1, rows[0].VisibleGroupSize);
        Assert.Equal(0, ShaLanes.LaneCount(rows));
    }

    [Fact]
    public void HiddenMemberBetweenTwoVisibleOnes_StillConnects() {
        var all = new List<VersionKey> { Key("aa"), Key("aa"), Key("aa") };
        var visible = new List<VersionKey> { all[0], all[2] };
        var rows = ShaLanes.Build([0L, 2L], visible, all);

        Assert.Equal(ShaLaneMark.Start, MarkAt(rows, 0, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(rows, 2, 0));
        Assert.Equal(3, rows[0].GroupSize);
        Assert.Equal(2, rows[0].VisibleGroupSize);
    }

    [Fact]
    public void BlankAndNullShas_NeverGroup() {
        var rows = Build(null, "", "   ", null);

        Assert.All(rows.Values, row => Assert.Empty(row.Segments));
        Assert.All(rows.Values, row => Assert.Equal(0, row.GroupSize));
        Assert.Equal(0, ShaLanes.LaneCount(rows));
    }

    [Fact]
    public void ShaMatchIsCaseInsensitive() {
        var rows = Build("AA", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(rows, 0, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(rows, 1, 0));
    }

    [Fact]
    public void MismatchedInputLengths_ReturnNothing() {
        var visible = new List<VersionKey> { Key("aa"), Key("aa") };

        Assert.Empty(ShaLanes.Build([0L], visible, visible));
    }
}

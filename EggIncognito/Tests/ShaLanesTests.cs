using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class ShaLanesTests {
    private static VersionKey Key(string? sha, string platform = "ios") =>
        new(platform, "1.0.0", "100", null, null, null, null, sha);

    private static ShaLaneLayout Build(params string?[] shas) {
        var visible = shas.Select(s => Key(s)).ToList();
        var keys = Enumerable.Range(0, shas.Length).Select(i => (long)i).ToList();
        return ShaLanes.Build(keys, visible, visible);
    }

    private static ShaLaneMark MarkAt(ShaLaneLayout layout, long key, int lane) =>
        layout.Rows[key].Segments.FirstOrDefault(s => s.Lane == lane)?.Mark ?? ShaLaneMark.None;

    [Fact]
    public void AdjacentPair_DrawsStartThenEndOnLaneZero() {
        var layout = Build("aa", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(layout, 0, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(layout, 1, 0));
        Assert.Equal(1, ShaLanes.LaneCount(layout));
    }

    [Fact]
    public void NonAdjacentPair_PassesThroughTheRowsBetween() {
        var layout = Build("aa", "bb", "cc", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(layout, 0, 0));
        Assert.Equal(ShaLaneMark.Pass, MarkAt(layout, 1, 0));
        Assert.Equal(ShaLaneMark.Pass, MarkAt(layout, 2, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(layout, 3, 0));
    }

    [Fact]
    public void ThreeMembers_MarkTheMiddleAsNode() {
        var layout = Build("aa", "aa", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(layout, 0, 0));
        Assert.Equal(ShaLaneMark.Node, MarkAt(layout, 1, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(layout, 2, 0));
    }

    [Fact]
    public void OverlappingGroups_TakeSeparateLanes() {
        var layout = Build("aa", "bb", "aa", "bb");

        Assert.Equal(0, layout.Rows[0].Segments.Single().Lane);
        Assert.Equal(1, layout.Rows[1].Segments.Single(s => s.Mark == ShaLaneMark.Start).Lane);
        Assert.Equal(2, ShaLanes.LaneCount(layout));
    }

    [Fact]
    public void DisjointGroups_ShareOneLane() {
        var layout = Build("aa", "aa", "bb", "bb");

        Assert.Equal(0, layout.Rows[2].Segments.Single().Lane);
        Assert.Equal(1, ShaLanes.LaneCount(layout));
    }

    [Fact]
    public void DisjointGroups_GetDistinctGroupIdsAndShas() {
        var layout = Build("aa", "aa", "bb", "bb");

        int? groupIdAa = layout.Rows[0].GroupId;
        int? groupIdBb = layout.Rows[2].GroupId;

        Assert.NotNull(groupIdAa);
        Assert.NotNull(groupIdBb);
        Assert.NotEqual(groupIdAa, groupIdBb);
        Assert.Equal("aa", layout.GroupShas[groupIdAa!.Value]);
        Assert.Equal("bb", layout.GroupShas[groupIdBb!.Value]);
    }

    [Fact]
    public void PassThroughRow_KeepsNullGroupIdButSegmentCarriesCrossingGroup() {
        var layout = Build("aa", "bb", "cc", "aa");

        int? groupId = layout.Rows[0].GroupId;

        Assert.NotNull(groupId);
        Assert.Equal(groupId, layout.Rows[3].GroupId);
        Assert.Null(layout.Rows[1].GroupId);
        Assert.Equal(groupId, layout.Rows[1].Segments.Single().GroupId);
    }

    [Fact]
    public void SingleMemberGroups_GetNoLaneAndNullGroupId() {
        var layout = Build("aa", "bb", "cc");

        Assert.All(layout.Rows.Values, row => Assert.Empty(row.Segments));
        Assert.All(layout.Rows.Values, row => Assert.Null(row.GroupId));
        Assert.Empty(layout.GroupShas);
        Assert.Equal(0, ShaLanes.LaneCount(layout));
    }

    [Fact]
    public void FiveOverlappingGroups_StopAtTheLaneCap() {
        var layout = Build("aa", "bb", "cc", "dd", "ee", "aa", "bb", "cc", "dd", "ee");

        Assert.Equal(4, ShaLanes.LaneCount(layout));
        Assert.All(layout.Rows[4].Segments, s => Assert.Equal(ShaLaneMark.Pass, s.Mark));
        Assert.Empty(layout.Rows[9].Segments);
        Assert.Equal(2, layout.Rows[4].VisibleGroupSize);
        Assert.Null(layout.Rows[4].GroupId);
    }

    [Fact]
    public void GroupReducedToOneByFiltering_DrawsNothingButKeepsTheHiddenCount() {
        var all = new List<VersionKey> { Key("aa"), Key("aa"), Key("bb") };
        var visible = new List<VersionKey> { all[0], all[2] };
        var layout = ShaLanes.Build([0L, 2L], visible, all);

        Assert.Empty(layout.Rows[0].Segments);
        Assert.Equal(2, layout.Rows[0].GroupSize);
        Assert.Equal(1, layout.Rows[0].VisibleGroupSize);
        Assert.Null(layout.Rows[0].GroupId);
        Assert.Equal(0, ShaLanes.LaneCount(layout));
    }

    [Fact]
    public void HiddenMemberBetweenTwoVisibleOnes_StillConnects() {
        var all = new List<VersionKey> { Key("aa"), Key("aa"), Key("aa") };
        var visible = new List<VersionKey> { all[0], all[2] };
        var layout = ShaLanes.Build([0L, 2L], visible, all);

        Assert.Equal(ShaLaneMark.Start, MarkAt(layout, 0, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(layout, 2, 0));
        Assert.Equal(3, layout.Rows[0].GroupSize);
        Assert.Equal(2, layout.Rows[0].VisibleGroupSize);
    }

    [Fact]
    public void BlankAndNullShas_NeverGroup() {
        var layout = Build(null, "", "   ", null);

        Assert.All(layout.Rows.Values, row => Assert.Empty(row.Segments));
        Assert.All(layout.Rows.Values, row => Assert.Equal(0, row.GroupSize));
        Assert.All(layout.Rows.Values, row => Assert.Null(row.GroupId));
        Assert.Empty(layout.GroupShas);
        Assert.Equal(0, ShaLanes.LaneCount(layout));
    }

    [Fact]
    public void ShaMatchIsCaseInsensitive() {
        var layout = Build("AA", "aa");

        Assert.Equal(ShaLaneMark.Start, MarkAt(layout, 0, 0));
        Assert.Equal(ShaLaneMark.End, MarkAt(layout, 1, 0));
    }

    [Fact]
    public void MismatchedInputLengths_ReturnNothing() {
        var visible = new List<VersionKey> { Key("aa"), Key("aa") };
        var layout = ShaLanes.Build([0L], visible, visible);

        Assert.Empty(layout.Rows);
        Assert.Empty(layout.GroupShas);
    }
}

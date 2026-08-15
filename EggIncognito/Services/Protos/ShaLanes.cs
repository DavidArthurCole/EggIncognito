using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Protos;

public enum ShaLaneMark { None, Start, Node, End, Pass }

public sealed record ShaLaneSegment(int Lane, ShaLaneMark Mark);

public sealed record ShaLaneRow(IReadOnlyList<ShaLaneSegment> Segments, int GroupSize, int VisibleGroupSize);

public static class ShaLanes {
    public static IReadOnlyDictionary<long, ShaLaneRow> Build(
        IReadOnlyList<long> orderedKeys, IReadOnlyList<VersionKey> orderedVisible,
        IReadOnlyList<VersionKey> all, int maxLanes = 4) {
        var rows = new Dictionary<long, ShaLaneRow>();
        if (orderedKeys.Count != orderedVisible.Count) return rows;

        List<List<int>> groups = GroupIndexes(orderedVisible);
        var segments = new List<ShaLaneSegment>[orderedVisible.Count];
        for (var ix = 0; ix < segments.Length; ix++) segments[ix] = [];

        var laneEnd = new List<int>();
        foreach (List<int> group in groups.Where(g => g.Count > 1).OrderBy(g => g[0])) {
            int first = group[0];
            int last = group[^1];
            int lane = FreeLane(laneEnd, first, last, maxLanes);
            if (lane < 0) continue;

            var members = new HashSet<int>(group);
            for (int ix = first; ix <= last; ix++) {
                ShaLaneMark mark = ix == first ? ShaLaneMark.Start
                    : ix == last ? ShaLaneMark.End
                    : members.Contains(ix) ? ShaLaneMark.Node
                    : ShaLaneMark.Pass;
                segments[ix].Add(new ShaLaneSegment(lane, mark));
            }
        }

        var visibleSize = new int[orderedVisible.Count];
        foreach (List<int> group in groups) {
            foreach (int ix in group) visibleSize[ix] = group.Count;
        }

        int[] totalSize = TotalSizes(orderedVisible, all);
        for (var ix = 0; ix < orderedKeys.Count; ix++) {
            rows[orderedKeys[ix]] = new ShaLaneRow(segments[ix], totalSize[ix], visibleSize[ix]);
        }

        return rows;
    }

    public static int LaneCount(IReadOnlyDictionary<long, ShaLaneRow> rows) {
        var top = -1;
        foreach (ShaLaneRow row in rows.Values) {
            foreach (ShaLaneSegment segment in row.Segments) {
                if (segment.Lane > top) top = segment.Lane;
            }
        }

        return top + 1;
    }

    private static int FreeLane(List<int> laneEnd, int first, int last, int maxLanes) {
        for (var lane = 0; lane < laneEnd.Count; lane++) {
            if (laneEnd[lane] >= first) continue;
            laneEnd[lane] = last;
            return lane;
        }

        if (laneEnd.Count >= maxLanes) return -1;

        laneEnd.Add(last);
        return laneEnd.Count - 1;
    }

    private static List<List<int>> GroupIndexes(IReadOnlyList<VersionKey> keys) {
        var groups = new List<List<int>>();
        var leaders = new List<VersionKey>();
        for (var ix = 0; ix < keys.Count; ix++) {
            VersionKey key = keys[ix];
            if (string.IsNullOrWhiteSpace(key.ProtoSha)) continue;

            var joined = false;
            for (var g = 0; g < leaders.Count; g++) {
                if (!ProtoVersionTranslator.SharesProtoSha(leaders[g], key)) continue;

                groups[g].Add(ix);
                joined = true;
                break;
            }

            if (joined) continue;

            leaders.Add(key);
            groups.Add([ix]);
        }

        return groups;
    }

    private static int[] TotalSizes(IReadOnlyList<VersionKey> visible, IReadOnlyList<VersionKey> all) {
        List<List<int>> groups = GroupIndexes(all);
        var sizes = new int[visible.Count];
        for (var ix = 0; ix < visible.Count; ix++) {
            VersionKey key = visible[ix];
            if (string.IsNullOrWhiteSpace(key.ProtoSha)) continue;

            foreach (List<int> group in groups) {
                if (!ProtoVersionTranslator.SharesProtoSha(all[group[0]], key)) continue;

                sizes[ix] = group.Count;
                break;
            }
        }

        return sizes;
    }
}

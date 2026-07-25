namespace EggIncognito.Services.ProtoExtract;

public static class PlacementSolver {
    public static SolveResult Solve(SolveRequest req) {
        if (req.Pos is not { Length: 3 } || req.RotDeg is not { Length: 3 })
            return new SolveResult(req.Pos, false, "invalid request");

        float x = req.Pos[0], y = req.Pos[1], z = req.Pos[2];
        bool adjusted = false;
        string reason = "ok";

        if (req.GridSize > 0) {
            float sx = SnapTo(x, req.GridSize);
            float sz = SnapTo(z, req.GridSize);
            if (sx != x || sz != z) adjusted = true;
            x = sx;
            z = sz;
        }


        if (req.ClampFloor) {
            float worldMinY = y + req.LocalMinY * req.Scale;
            if (Math.Abs(worldMinY) > 1e-4f) {
                y -= worldMinY;
                adjusted = true;
            }
        }


        if (req.Others.Length > 0) {
            var foot = WorldFootprint(req.LocalFootprint, x, z, req.RotDeg[1], req.Scale);
            if (UnionOfOverlaps(foot, req.Others) is not null) reason = "overlap";
        }

        if (req.ZoneLocked && !ZoneLayout.IsInsideAnyZone(x, z))
            reason = "outside-zone";

        return new SolveResult([x, y, z], adjusted, reason);
    }

    private static float SnapTo(float v, float cell) => (float)(Math.Round(v / cell) * cell);


    public static Box2 WorldFootprint(Box2 local, float x, float z, float rotYDeg, float scale) {
        float hx = local.Width * 0.5f * scale;
        float hz = local.Depth * 0.5f * scale;

        float cx = (local.MinX + local.MaxX) * 0.5f * scale;
        float cz = (local.MinZ + local.MaxZ) * 0.5f * scale;

        float a = rotYDeg * (float)Math.PI / 180f;
        float c = Math.Abs((float)Math.Cos(a)), s = Math.Abs((float)Math.Sin(a));

        float rhx = c * hx + s * hz;
        float rhz = s * hx + c * hz;

        float rc = (float)Math.Cos(a), rs = (float)Math.Sin(a);
        float rcx = cx * rc - cz * rs;
        float rcz = cx * rs + cz * rc;

        float ox = x + rcx, oz = z + rcz;
        return new Box2(ox - rhx, ox + rhx, oz - rhz, oz + rhz);
    }


    private static Box2? UnionOfOverlaps(Box2 foot, Box2[] others) {
        bool any = false;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var o in others) {
            if (!foot.Intersects(o)) continue;
            any = true;
            minX = Math.Min(minX, o.MinX);
            maxX = Math.Max(maxX, o.MaxX);
            minZ = Math.Min(minZ, o.MinZ);
            maxZ = Math.Max(maxZ, o.MaxZ);
        }

        return any ? new Box2(minX, maxX, minZ, maxZ) : null;
    }


    public static GridResult SnapToGrid(Box2 localFootprint, float scale, float x, float z, float cell,
        IReadOnlySet<Cell> occupied) {
        if (cell <= 0) return new GridResult([], x, z, true);

        int spanC = Math.Max(1, (int)Math.Ceiling(localFootprint.Width * scale / cell - 1e-3));
        int spanR = Math.Max(1, (int)Math.Ceiling(localFootprint.Depth * scale / cell - 1e-3));


        int col0 = (int)Math.Round(x / cell - spanC / 2.0);
        int row0 = (int)Math.Round(z / cell - spanR / 2.0);

        var cells = new List<Cell>(spanC * spanR);
        bool valid = true;
        for (int dc = 0; dc < spanC; dc++) {
            for (int dr = 0; dr < spanR; dr++) {
                var c = new Cell(col0 + dc, row0 + dr);
                cells.Add(c);
                if (occupied.Contains(c)) valid = false;
            }
        }

        float centerX = (col0 + spanC / 2.0f) * cell;
        float centerZ = (row0 + spanR / 2.0f) * cell;
        return new GridResult(cells, centerX, centerZ, valid);
    }


    public static IEnumerable<Cell> CellsOf(Box2 localFootprint, float scale, float centerX, float centerZ,
        float cell) {
        if (cell <= 0) yield break;
        int spanC = Math.Max(1, (int)Math.Ceiling(localFootprint.Width * scale / cell - 1e-3));
        int spanR = Math.Max(1, (int)Math.Ceiling(localFootprint.Depth * scale / cell - 1e-3));
        int col0 = (int)Math.Round(centerX / cell - spanC / 2.0);
        int row0 = (int)Math.Round(centerZ / cell - spanR / 2.0);
        for (int dc = 0; dc < spanC; dc++) {
            for (int dr = 0; dr < spanR; dr++)
                yield return new Cell(col0 + dc, row0 + dr);
        }
    }


    public static IReadOnlyList<Move> DominoNudge(GridBox changed, IReadOnlyList<GridBox> others) {
        var boxes = others.ToDictionary(b => b.Id, b => b);
        var delta = new Dictionary<string, (int dc, int dr)>();

        var queue = new Queue<GridBox>();
        queue.Enqueue(changed);

        int guard = 0, maxIterations = 4 * (others.Count + 1) * (others.Count + 1) + 16;
        while (queue.Count > 0 && guard++ < maxIterations) {
            var mover = queue.Dequeue();
            foreach (string id in boxes.Keys.ToList()) {
                if (id == mover.Id) continue;
                var b = boxes[id];
                if (!mover.Overlaps(b)) continue;

                (int dc, int dr) = PushAway(mover, b);
                var moved = b.Shift(dc, dr);
                boxes[id] = moved;
                var prev = delta.TryGetValue(id, out var p) ? p : (0, 0);
                delta[id] = (prev.Item1 + dc, prev.Item2 + dr);
                queue.Enqueue(moved);
            }
        }

        return delta.Select(kv => new Move(kv.Key, kv.Value.dc, kv.Value.dr)).ToList();
    }


    private static (int dc, int dr) PushAway(GridBox mover, GridBox b) {
        int overlapX = Math.Min(mover.Right, b.Right) - Math.Max(mover.Col, b.Col);
        int overlapZ = Math.Min(mover.Bottom, b.Bottom) - Math.Max(mover.Row, b.Row);
        bool bCenterRightOfMover = b.Col + b.Right >= mover.Col + mover.Right;
        bool bCenterBelowMover = b.Row + b.Bottom >= mover.Row + mover.Bottom;
        if (overlapX <= overlapZ)
            return (bCenterRightOfMover ? overlapX : -overlapX, 0);
        return (0, bCenterBelowMover ? overlapZ : -overlapZ);
    }


    public readonly record struct Box2(float MinX, float MaxX, float MinZ, float MaxZ) {
        public float Width => MaxX - MinX;
        public float Depth => MaxZ - MinZ;
        public bool Intersects(Box2 o) => MinX < o.MaxX && MaxX > o.MinX && MinZ < o.MaxZ && MaxZ > o.MinZ;
    }

    public sealed record SolveRequest(
        float[] Pos,
        float[] RotDeg,
        float Scale,
        Box2 LocalFootprint,
        float LocalMinY,
        Box2[] Others,
        float GridSize,
        bool ClampFloor = true,
        bool ZoneLocked = false);

    public sealed record SolveResult(float[] Pos, bool Adjusted, string Reason);


    public readonly record struct Cell(int Col, int Row);


    public sealed record GridResult(IReadOnlyList<Cell> Cells, float CenterX, float CenterZ, bool Valid);


    public readonly record struct GridBox(string Id, int Col, int Row, int SpanC, int SpanR) {
        public int Right => Col + SpanC;
        public int Bottom => Row + SpanR;
        public bool Overlaps(GridBox o) => Col < o.Right && Right > o.Col && Row < o.Bottom && Bottom > o.Row;
        public GridBox Shift(int dc, int dr) => this with { Col = Col + dc, Row = Row + dr };
    }


    public readonly record struct Move(string Id, int DeltaCol, int DeltaRow);
}

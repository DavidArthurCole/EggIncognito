namespace EggIncognito.Services.ProtoExtract;

// Corrects a proposed element placement so it (1) snaps to the design grid, (2) never sits below the floor, and
// (3) does not overlap another element's ground footprint. Pure + deterministic so the playground designer can
// hand it a dragged transform and get back a legal one, fully unit-tested without any renderer. The designer JS
// mirrors the cheap parts (grid + floor) for a live preview; this is the authoritative resolve on drop.
//
// Footprints are axis-aligned ground rectangles (the farm is effectively top-down). A yawed element widens its
// rect to the rotated corners' extent: an approximation, not an exact oriented box, which is enough to keep
// buildings from visibly interpenetrating. Vertical collision is floor-only (no stacking).
public static class PlacementSolver
{
    // An element's untransformed ground rectangle, centered on its local origin: half-extents along X/Z. The
    // engine derives this from the mesh's local bbox once, then reuses it at any transform.
    public readonly record struct Box2(float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public float Width => MaxX - MinX;
        public float Depth => MaxZ - MinZ;
        public bool Intersects(Box2 o) => MinX < o.MaxX && MaxX > o.MinX && MinZ < o.MaxZ && MaxZ > o.MinZ;
    }

    public sealed record SolveRequest(
        float[] Pos, float[] RotDeg, float Scale,
        Box2 LocalFootprint, // the element's untransformed ground rect (local origin centered)
        float LocalMinY, // the element's untransformed lowest point (negative = below its origin)
        Box2[] Others, // every OTHER element's world ground rect
        float GridSize, // grid cell size; 0 = no snapping
        bool ClampFloor = true, // ground buildings rest on y=0; false for pinned/backdrop pieces
        bool ZoneLocked = false); // true for buildings/habs/silos: resolved (x,z) must land inside a zone

    public sealed record SolveResult(float[] Pos, bool Adjusted, string Reason);

    public static SolveResult Solve(SolveRequest req)
    {
        if (req.Pos is not { Length: 3 } || req.RotDeg is not { Length: 3 })
            return new SolveResult(req.Pos, false, "invalid request");

        float x = req.Pos[0], y = req.Pos[1], z = req.Pos[2];
        bool adjusted = false;
        string reason = "ok";

        if (req.GridSize > 0)
        {
            var sx = SnapTo(x, req.GridSize);
            var sz = SnapTo(z, req.GridSize);
            if (sx != x || sz != z) adjusted = true;
            x = sx; z = sz;
        }

        // floor clamp: raise Y so the element's lowest point sits exactly on y=0. For a ground building this
        // both lifts it out of the floor AND drops it down onto the floor (no floating), so a placed building
        // always rests on the ground. Pinned pieces (ClampFloor=false) keep their authored Y.
        if (req.ClampFloor)
        {
            var worldMinY = y + req.LocalMinY * req.Scale;
            if (Math.Abs(worldMinY) > 1e-4f) { y -= worldMinY; adjusted = true; }
        }

        // No overlap PUSH here: overlap is owned by the block-grid path (SnapToGrid + the designer, which blocks an
        // occupied drop instead of relocating it). The old union-push flung pieces across the scene when a
        // self-placing mesh reported a large off-origin footprint. Solve now only snaps to grid + clamps the floor.
        // Others is kept on the request for callers that still want an informational overlap flag.
        if (req.Others.Length > 0)
        {
            var foot = WorldFootprint(req.LocalFootprint, x, z, req.RotDeg[1], req.Scale);
            if (UnionOfOverlaps(foot, req.Others) is not null) reason = "overlap";
        }

        if (req.ZoneLocked && !ZoneLayout.IsInsideAnyZone(x, z))
            reason = "outside-zone";

        return new SolveResult([x, y, z], adjusted, reason);
    }

    private static float SnapTo(float v, float cell) => (float)(Math.Round(v / cell) * cell);

    // The element's world ground rect: the local box scaled, yaw-rotated (widened to the rotated corners), then
    // translated to (x, z). Yaw only; the farm is top-down so pitch/roll do not affect the footprint.
    public static Box2 WorldFootprint(Box2 local, float x, float z, float rotYDeg, float scale)
    {
        float hx = local.Width * 0.5f * scale;
        float hz = local.Depth * 0.5f * scale;
        // local box may be off-center (origin not at its middle); carry its center offset through the rotation.
        float cx = (local.MinX + local.MaxX) * 0.5f * scale;
        float cz = (local.MinZ + local.MaxZ) * 0.5f * scale;

        float a = rotYDeg * (float)Math.PI / 180f;
        float c = Math.Abs((float)Math.Cos(a)), s = Math.Abs((float)Math.Sin(a));
        // rotated AABB half-extents (the classic |cos|*hx + |sin|*hz widening).
        float rhx = c * hx + s * hz;
        float rhz = s * hx + c * hz;
        // rotate the center offset too.
        float rc = (float)Math.Cos(a), rs = (float)Math.Sin(a);
        float rcx = cx * rc - cz * rs;
        float rcz = cx * rs + cz * rc;

        float ox = x + rcx, oz = z + rcz;
        return new Box2(ox - rhx, ox + rhx, oz - rhz, oz + rhz);
    }

    // The bounding rect of every `other` that the footprint currently overlaps, or null when it overlaps none.
    // Pushing out of the union escapes a piece boxed between several neighbors along whichever axis is shortest.
    private static Box2? UnionOfOverlaps(Box2 foot, Box2[] others)
    {
        bool any = false;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var o in others)
        {
            if (!foot.Intersects(o)) continue;
            any = true;
            minX = Math.Min(minX, o.MinX); maxX = Math.Max(maxX, o.MaxX);
            minZ = Math.Min(minZ, o.MinZ); maxZ = Math.Max(maxZ, o.MaxZ);
        }
        return any ? new Box2(minX, maxX, minZ, maxZ) : null;
    }

    // A grid cell on the ground plane (integer column/row at cell-size resolution). Cell (c,r) covers world
    // [c*cell, (c+1)*cell) x [r*cell, (r+1)*cell).
    public readonly record struct Cell(int Col, int Row);

    // The result of snapping an element to the block grid: the cells it would occupy, the snapped CENTER world
    // position to render it at, and whether every target cell is free (Valid). The designer highlights Cells
    // green when Valid, red otherwise, and only commits the drop when Valid.
    public sealed record GridResult(IReadOnlyList<Cell> Cells, float CenterX, float CenterZ, bool Valid);

    // Snap an element to the block grid. The element occupies ceil(width/cell) x ceil(depth/cell) cells (auto from
    // its footprint). Its block is centered on the proposed (x,z) by snapping the block's CENTER to the nearest
    // cell-boundary that keeps an integer cell span centered, then the occupied cells are listed and checked
    // against `occupied`. No pushing: an invalid drop is reported invalid (the caller reverts), never flung.
    public static GridResult SnapToGrid(Box2 localFootprint, float scale, float x, float z, float cell,
        IReadOnlySet<Cell> occupied)
    {
        if (cell <= 0) return new GridResult([], x, z, true);

        int spanC = Math.Max(1, (int)Math.Ceiling(localFootprint.Width * scale / cell - 1e-3));
        int spanR = Math.Max(1, (int)Math.Ceiling(localFootprint.Depth * scale / cell - 1e-3));

        // place the block so its center is nearest (x,z). For an even span the center sits on a cell boundary; for
        // an odd span it sits at a cell center. Snap the block's min-corner column/row accordingly.
        int col0 = (int)Math.Round(x / cell - spanC / 2.0);
        int row0 = (int)Math.Round(z / cell - spanR / 2.0);

        var cells = new List<Cell>(spanC * spanR);
        bool valid = true;
        for (int dc = 0; dc < spanC; dc++)
            for (int dr = 0; dr < spanR; dr++)
            {
                var c = new Cell(col0 + dc, row0 + dr);
                cells.Add(c);
                if (occupied.Contains(c)) valid = false;
            }

        // the block's world center = its min corner + half the span, in cell units.
        float centerX = (col0 + spanC / 2.0f) * cell;
        float centerZ = (row0 + spanR / 2.0f) * cell;
        return new GridResult(cells, centerX, centerZ, valid);
    }

    // The cells an already-placed element occupies, given its world center + footprint + the cell size. Used to
    // build the `occupied` set from every OTHER element so a drop knows which cells are taken.
    public static IEnumerable<Cell> CellsOf(Box2 localFootprint, float scale, float centerX, float centerZ, float cell)
    {
        if (cell <= 0) yield break;
        int spanC = Math.Max(1, (int)Math.Ceiling(localFootprint.Width * scale / cell - 1e-3));
        int spanR = Math.Max(1, (int)Math.Ceiling(localFootprint.Depth * scale / cell - 1e-3));
        int col0 = (int)Math.Round(centerX / cell - spanC / 2.0);
        int row0 = (int)Math.Round(centerZ / cell - spanR / 2.0);
        for (int dc = 0; dc < spanC; dc++)
            for (int dr = 0; dr < spanR; dr++)
                yield return new Cell(col0 + dc, row0 + dr);
    }

    // An element on the grid for the domino pass: its id + the integer cell rectangle it occupies.
    public readonly record struct GridBox(string Id, int Col, int Row, int SpanC, int SpanR)
    {
        public int Right => Col + SpanC; // exclusive
        public int Bottom => Row + SpanR; // exclusive
        public bool Overlaps(GridBox o) => Col < o.Right && Right > o.Col && Row < o.Bottom && Bottom > o.Row;
        public GridBox Shift(int dc, int dr) => this with { Col = Col + dc, Row = Row + dr };
    }

    // The cell offset to apply to one element after a domino pass.
    public readonly record struct Move(string Id, int DeltaCol, int DeltaRow);

    // When `changed` grows (e.g. a tier swap to a bigger building) and now overlaps neighbors, push each
    // overlapping neighbor directly AWAY from `changed` along the axis of least overlap, by just enough cells to
    // clear, and CASCADE: a pushed neighbor that now hits a further element pushes that one too (the domino).
    // Pure integer-cell logic. `changed` stays put; everything else may move. Returns the net per-element offset
    // (only elements that actually moved). Deterministic + bounded (no infinite cascade).
    public static IReadOnlyList<Move> DominoNudge(GridBox changed, IReadOnlyList<GridBox> others)
    {
        // work on a mutable copy keyed by id; track net deltas.
        var boxes = others.ToDictionary(b => b.Id, b => b);
        var delta = new Dictionary<string, (int dc, int dr)>();

        var queue = new Queue<GridBox>();
        queue.Enqueue(changed);

        int guard = 0, maxIterations = 4 * (others.Count + 1) * (others.Count + 1) + 16;
        while (queue.Count > 0 && guard++ < maxIterations)
        {
            var mover = queue.Dequeue();
            foreach (var id in boxes.Keys.ToList())
            {
                if (id == mover.Id) continue;
                var b = boxes[id];
                if (!mover.Overlaps(b)) continue;

                var (dc, dr) = PushAway(mover, b);
                var moved = b.Shift(dc, dr);
                boxes[id] = moved;
                var prev = delta.TryGetValue(id, out var p) ? p : (0, 0);
                delta[id] = (prev.Item1 + dc, prev.Item2 + dr);
                queue.Enqueue(moved); // its new spot may shove the next element along
            }
        }

        return delta.Select(kv => new Move(kv.Key, kv.Value.dc, kv.Value.dr)).ToList();
    }

    // The minimal whole-cell shift that moves `b` out of `mover`, directly away along the shallower overlap axis
    // (so a wider building shoves its neighbor straight aside, the domino direction).
    private static (int dc, int dr) PushAway(GridBox mover, GridBox b)
    {
        int overlapX = Math.Min(mover.Right, b.Right) - Math.Max(mover.Col, b.Col);
        int overlapZ = Math.Min(mover.Bottom, b.Bottom) - Math.Max(mover.Row, b.Row);
        bool bCenterRightOfMover = (b.Col + b.Right) >= (mover.Col + mover.Right);
        bool bCenterBelowMover = (b.Row + b.Bottom) >= (mover.Row + mover.Bottom);
        if (overlapX <= overlapZ)
            return (bCenterRightOfMover ? overlapX : -overlapX, 0);
        return (0, bCenterBelowMover ? overlapZ : -overlapZ);
    }
}

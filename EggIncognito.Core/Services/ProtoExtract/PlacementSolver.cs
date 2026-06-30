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
        bool ClampFloor = true); // ground buildings rest on y=0; false for pinned/backdrop pieces

    public sealed record SolveResult(float[] Pos, bool Adjusted, string Reason);

    private const int MaxOverlapIterations = 16;

    public static SolveResult Solve(SolveRequest req)
    {
        if (req.Pos is not { Length: 3 } || req.RotDeg is not { Length: 3 })
            return new SolveResult(req.Pos, false, "invalid request");

        float x = req.Pos[0], y = req.Pos[1], z = req.Pos[2];
        bool adjusted = false;
        string reason = "ok";

        // 1) grid snap on the ground plane.
        if (req.GridSize > 0)
        {
            var sx = SnapTo(x, req.GridSize);
            var sz = SnapTo(z, req.GridSize);
            if (sx != x || sz != z) adjusted = true;
            x = sx; z = sz;
        }

        // 2) floor clamp: raise Y so the element's lowest point sits exactly on y=0. For a ground building this
        //    both lifts it out of the floor AND drops it down onto the floor (no floating), so a placed building
        //    always rests on the ground. Pinned pieces (ClampFloor=false) keep their authored Y.
        if (req.ClampFloor)
        {
            var worldMinY = y + req.LocalMinY * req.Scale;
            if (Math.Abs(worldMinY) > 1e-4f) { y -= worldMinY; adjusted = true; }
        }

        // 3) overlap resolution: push clear of every overlapping footprint. Each iteration resolves against the
        //    UNION of all currently-overlapping others so a piece hemmed on one axis escapes along the other,
        //    instead of oscillating between two neighbors forever.
        var foot = WorldFootprint(req.LocalFootprint, x, z, req.RotDeg[1], req.Scale);
        int iter = 0;
        while (iter++ < MaxOverlapIterations)
        {
            if (UnionOfOverlaps(foot, req.Others) is not { } union) break;
            (x, z) = PushOut(foot, union, x, z);
            if (req.GridSize > 0) { x = SnapTo(x, req.GridSize); z = SnapTo(z, req.GridSize); }
            foot = WorldFootprint(req.LocalFootprint, x, z, req.RotDeg[1], req.Scale);
            adjusted = true;
        }
        if (UnionOfOverlaps(foot, req.Others) is not null)
            reason = "blocked";

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

    // Move (x,z) so `foot` clears `other` along the axis of least penetration (minimum translation vector).
    private static (float, float) PushOut(Box2 foot, Box2 other, float x, float z)
    {
        float pushLeft = foot.MaxX - other.MinX; // move -X by this to clear
        float pushRight = other.MaxX - foot.MinX; // move +X
        float pushDown = foot.MaxZ - other.MinZ; // move -Z
        float pushUp = other.MaxZ - foot.MinZ; // move +Z

        float dxNeg = pushLeft, dxPos = pushRight, dzNeg = pushDown, dzPos = pushUp;
        float minX = Math.Min(dxNeg, dxPos);
        float minZ = Math.Min(dzNeg, dzPos);

        if (minX <= minZ)
            return dxNeg <= dxPos ? (x - dxNeg, z) : (x + dxPos, z);
        return dzNeg <= dzPos ? (x, z - dzNeg) : (x, z + dzPos);
    }
}

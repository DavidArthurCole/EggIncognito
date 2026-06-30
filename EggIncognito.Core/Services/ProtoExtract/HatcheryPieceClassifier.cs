namespace EggIncognito.Services.ProtoExtract;

// Classifies a hatchery floating sub-mesh into the role the game animates it as, READ FROM ITS AUTHORED BOUNDS
// (the .rpo geometry), not guessed. Two families decide everything:
//
//   * origin-centered piece (bbox center ~ 0): authored at the local origin, so the game POSITIONS it at runtime
//     (orbit / spin / beam around the body anchor). Sub-roles by shape:
//       - Beam:  a thin vertical spike (Y-extent >> X/Z) = the beam fired probe->orb (universe bolt).
//       - Ring:  flat + wide (X/Y wide, Z thin) = a spinning ring (darkmatter ring_1/2/3).
//       - Orb:   tiny near-cube (enlightenment orb) = a small floating core.
//       - Probe/Shell: a disc or sphere = an orbiting satellite (universe probe) or a nested shell (vision).
//   * world-placed piece (bbox center far from origin, inside the body's X span): authored AT its spot on the
//     body (ai top_0..3 across the roof, graviton top). Rendered static at its authored position, no orbit.
//
// The role drives the renderer: world-placed -> draw as-is; centered -> drive with the extracted state machine.
public static class HatcheryPieceClassifier
{
    public enum Role { WorldPlaced, Beam, Ring, Orb, Probe, Shell }

    public readonly record struct Bounds(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
    {
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterY => (MinY + MaxY) * 0.5f;
        public float CenterZ => (MinZ + MaxZ) * 0.5f;
        public float ExtentX => MaxX - MinX;
        public float ExtentY => MaxY - MinY;
        public float ExtentZ => MaxZ - MinZ;
    }

    // A piece is "world-placed" when its bbox center sits well away from the local origin on the ground plane:
    // the mesh carries its real on-body position (ai roof spikes at x 8..18). Centered pieces sit at ~0.
    private const float CenteredRadius = 2.5f; // ground-plane distance from origin below which a piece is "centered"

    public static Role Classify(Bounds b)
    {
        var groundDist = MathF.Sqrt(b.CenterX * b.CenterX + b.CenterZ * b.CenterZ);
        if (groundDist > CenteredRadius) return Role.WorldPlaced;

        float ex = MathF.Abs(b.ExtentX), ey = MathF.Abs(b.ExtentY), ez = MathF.Abs(b.ExtentZ);
        float horiz = MathF.Max(ex, ez);

        // a thin vertical spike: tall, narrow on both ground axes (universe bolt: Y ~2, X/Z ~0.06).
        if (ey > 2f * horiz && ey > 0.5f) return Role.Beam;

        // a flat wide ring: broad on two axes, thin on the third (darkmatter ring: XY ~5, Z ~0.6).
        float min3 = MathF.Min(ex, MathF.Min(ey, ez));
        float max3 = MathF.Max(ex, MathF.Max(ey, ez));
        if (max3 > 1.5f && min3 < 0.5f * max3 && min3 < 1f) return Role.Ring;

        // a tiny core orb (enlightenment orb: ~0.15-0.2 across). Kept tighter than a probe disc so the universe
        // probe (~0.29 wide) does not fall in here.
        if (max3 < 0.22f) return Role.Orb;

        // wide-ish, roughly equal extents = a nested shell (vision middle/top, ~1.6/0.8). Smaller discs = probes.
        if (max3 > 1.2f) return Role.Shell;
        return Role.Probe;
    }
}

namespace EggIncognito.Services.ProtoExtract;

// Classifies a hatchery floating sub-mesh into the role the game animates it as, read from its authored bounds
// (the .rpo geometry), not guessed. An origin-centered piece (bbox center ~0) is positioned at runtime by shape
// (Beam/Ring/Orb/Probe/Shell); a world-placed piece (bbox center far from origin) is authored at its spot on the
// body and rendered static. The role drives the renderer: world-placed draws as-is, centered drives the
// extracted state machine.
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

    // A piece is "world-placed" when its bbox center sits well away from the local origin on the ground plane.
    private const float CenteredRadius = 2.5f;

    public static Role Classify(Bounds b)
    {
        var groundDist = MathF.Sqrt(b.CenterX * b.CenterX + b.CenterZ * b.CenterZ);
        if (groundDist > CenteredRadius) return Role.WorldPlaced;

        float ex = MathF.Abs(b.ExtentX), ey = MathF.Abs(b.ExtentY), ez = MathF.Abs(b.ExtentZ);
        float horiz = MathF.Max(ex, ez);

        // a thin vertical spike: tall, narrow on both ground axes.
        if (ey > 2f * horiz && ey > 0.5f) return Role.Beam;

        // a flat wide ring: broad on two axes, thin on the third.
        float min3 = MathF.Min(ex, MathF.Min(ey, ez));
        float max3 = MathF.Max(ex, MathF.Max(ey, ez));
        if (max3 > 1.5f && min3 < 0.5f * max3 && min3 < 1f) return Role.Ring;

        // a tiny core orb, kept tighter than a probe disc.
        if (max3 < 0.22f) return Role.Orb;

        // wide-ish, roughly equal extents = a nested shell; smaller discs = probes.
        if (max3 > 1.2f) return Role.Shell;
        return Role.Probe;
    }
}

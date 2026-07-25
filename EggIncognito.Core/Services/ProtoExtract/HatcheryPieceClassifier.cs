namespace EggIncognito.Services.ProtoExtract;

public static class HatcheryPieceClassifier {
    public enum Role {
        WorldPlaced,
        Beam,
        Ring,
        Orb,
        Probe,
        Shell
    }


    private const float CenteredRadius = 2.5f;

    public static Role Classify(Bounds b) {
        float groundDist = MathF.Sqrt(b.CenterX * b.CenterX + b.CenterZ * b.CenterZ);
        if (groundDist > CenteredRadius) return Role.WorldPlaced;

        float ex = MathF.Abs(b.ExtentX), ey = MathF.Abs(b.ExtentY), ez = MathF.Abs(b.ExtentZ);
        float horiz = MathF.Max(ex, ez);


        if (ey > 2f * horiz && ey > 0.5f) return Role.Beam;


        float min3 = MathF.Min(ex, MathF.Min(ey, ez));
        float max3 = MathF.Max(ex, MathF.Max(ey, ez));
        if (max3 > 1.5f && min3 < 0.5f * max3 && min3 < 1f) return Role.Ring;


        return max3 < 0.22f ? Role.Orb : max3 > 1.2f ? Role.Shell : Role.Probe;
    }

    public readonly record struct Bounds(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) {
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterY => (MinY + MaxY) * 0.5f;
        public float CenterZ => (MinZ + MaxZ) * 0.5f;
        public float ExtentX => MaxX - MinX;
        public float ExtentY => MaxY - MinY;
        public float ExtentZ => MaxZ - MinZ;
    }
}

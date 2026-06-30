using EggIncognito.Services.ProtoExtract;
using Xunit;
using static EggIncognito.Services.ProtoExtract.HatcheryPieceClassifier;

namespace EggIncognito.Tests.ProtoExtract;

// Classifies each hatchery floating piece into its animation role from the AUTHORED .rpo bounds. Cases use the
// real bounds pulled off the device (the /api/env/hatchery-dump Android run), so the classifier is pinned to the
// actual game geometry, not invented shapes.
public class HatcheryPieceClassifierTests
{
    private static Bounds B(float minX, float minY, float minZ, float maxX, float maxY, float maxZ) =>
        new(minX, minY, minZ, maxX, maxY, maxZ);

    [Fact]
    public void UniverseBolt_IsBeam()
    {
        // ei_hatchery_universe_bolt: a ~2-unit vertical spike, ±0.03 on the ground axes, centered at origin.
        var r = Classify(B(-0.0243f, -1.9485f, -0.0285f, 0.03f, 0.0515f, 0.0285f));
        Assert.Equal(Role.Beam, r);
    }

    [Fact]
    public void UniverseProbe_IsProbe()
    {
        // ei_hatchery_universe_probe: a small disc ±0.14, centered at origin.
        var r = Classify(B(-0.1461f, 0.0294f, -0.1439f, 0.1461f, 0.136f, 0.1439f));
        Assert.Equal(Role.Probe, r);
    }

    [Theory]
    [InlineData(-2.40095f, -2.400931f, -0.28836f, 2.400957f, 2.4009762f, 0.28825f)] // ring_1
    [InlineData(-2.665489f, -2.6654882f, -0.32009f, 2.66548f, 2.6654816f, 0.32005f)] // ring_2
    [InlineData(-2.9816837f, -2.981659f, -0.35808f, 2.9816837f, 2.981709f, 0.35799f)] // ring_3
    public void DarkmatterRings_AreRings(float a, float b, float c, float d, float e, float f)
    {
        Assert.Equal(Role.Ring, Classify(B(a, b, c, d, e, f)));
    }

    [Fact]
    public void EnlightenmentOrb_IsOrb()
    {
        // ei_hatchery_enlightenment_orb: tiny ±0.075/0.1, centered at origin.
        var r = Classify(B(-0.075f, -0.1f, -0.0866f, 0.075f, 0.1f, 0.0866f));
        Assert.Equal(Role.Orb, r);
    }

    [Theory]
    [InlineData(-1.6259466f, -0.6379837f, -1.6259466f, 1.6259465f, 0.64798f, 1.6259466f)] // vision_middle
    [InlineData(-0.8113817f, -0.6427114f, -0.8113817f, 0.8113818f, 0.65271f, 0.8113817f)] // vision_top
    public void VisionShells_AreShells(float a, float b, float c, float d, float e, float f)
    {
        Assert.Equal(Role.Shell, Classify(B(a, b, c, d, e, f)));
    }

    [Theory]
    [InlineData(8.7697735f, 2.5475159f, 1.4078898f, 10.586844f, 6.418156f, 4.5981183f)] // ai_top_0
    [InlineData(16.834557f, 2.5475159f, 1.4078898f, 18.651627f, 6.418156f, 4.5981183f)] // ai_top_3
    public void AiTops_AreWorldPlaced(float a, float b, float c, float d, float e, float f)
    {
        // authored across the roof at world X 8..18, not centered: render static at the authored spot.
        Assert.Equal(Role.WorldPlaced, Classify(B(a, b, c, d, e, f)));
    }

    [Fact]
    public void GravitonTop_IsWorldPlaced()
    {
        // ei_hatchery_graviton_top at world (8.8..12.6).
        var r = Classify(B(8.812026f, 4.0556645f, 1.0678458f, 12.617811f, 7.4451895f, 4.862035f));
        Assert.Equal(Role.WorldPlaced, r);
    }
}

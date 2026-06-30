using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

// The hatchery "floating effect" is separate sub-meshes (bolt/probe/rings/tops) hovering around the body, named
// ei_hatchery_<tier>[_<part>]. HatcheryEffectParts groups them programmatically. Tested against the real rpos
// stem list pulled from the device bundle (egginc 1.36).
public class HatcheryEffectPartsTests
{
    // a representative slice of the actual ei_hatchery_* rpos on the device.
    static readonly string[] Stems =
    [
        "ei_hatchery_universe", "ei_hatchery_universe_bolt", "ei_hatchery_universe_probe",
        "ei_hatchery_darkmatter", "ei_hatchery_darkmatter_ring_1", "ei_hatchery_darkmatter_ring_2",
        "ei_hatchery_darkmatter_ring_3",
        "ei_hatchery_ai", "ei_hatchery_ai_top_0", "ei_hatchery_ai_top_1", "ei_hatchery_ai_top_2",
        "ei_hatchery_ai_top_3",
        "ei_hatchery_vision", "ei_hatchery_vision_middle", "ei_hatchery_vision_top",
        "ei_hatchery_graviton", "ei_hatchery_graviton_top",
        "ei_hatchery_edible", "ei_hatchery_easter", "ei_hatchery_quantum",
        // non-hatchery noise that must be ignored:
        "ei_depot_3", "ei_lab_3", "hab_10k",
    ];

    [Fact]
    public void ForTier_Universe_BodyPlusBoltAndProbe()
    {
        var p = HatcheryEffectParts.ForTier(Stems, "universe");
        Assert.Equal("ei_hatchery_universe", p.Body);
        Assert.Contains("ei_hatchery_universe_bolt", p.Floating);
        Assert.Contains("ei_hatchery_universe_probe", p.Floating);
        Assert.Equal(2, p.Floating.Count);
    }

    [Fact]
    public void ForTier_Darkmatter_ThreeRings()
    {
        var p = HatcheryEffectParts.ForTier(Stems, "darkmatter");
        Assert.Equal("ei_hatchery_darkmatter", p.Body);
        Assert.Equal(3, p.Floating.Count);
        Assert.All(p.Floating, f => Assert.Contains("_ring_", f));
    }

    [Fact]
    public void ForTier_Ai_FourTops()
    {
        var p = HatcheryEffectParts.ForTier(Stems, "ai");
        Assert.Equal("ei_hatchery_ai", p.Body);
        Assert.Equal(4, p.Floating.Count); // ai_top_0..3
    }

    [Fact]
    public void ForTier_Vision_MiddleAndTop()
    {
        var p = HatcheryEffectParts.ForTier(Stems, "vision");
        Assert.Equal("ei_hatchery_vision", p.Body);
        Assert.Contains("ei_hatchery_vision_middle", p.Floating);
        Assert.Contains("ei_hatchery_vision_top", p.Floating);
    }

    [Fact]
    public void ForTier_Edible_NoFloatingParts()
    {
        var p = HatcheryEffectParts.ForTier(Stems, "edible");
        Assert.Equal("ei_hatchery_edible", p.Body);
        Assert.Empty(p.Floating);
    }

    [Fact]
    public void TierOf_StripsFloatingSuffix()
    {
        Assert.Equal("universe", HatcheryEffectParts.TierOf("ei_hatchery_universe"));
        Assert.Equal("universe", HatcheryEffectParts.TierOf("ei_hatchery_universe_bolt"));
        Assert.Equal("darkmatter", HatcheryEffectParts.TierOf("ei_hatchery_darkmatter_ring_2"));
        Assert.Equal("ai", HatcheryEffectParts.TierOf("ei_hatchery_ai_top_3"));
        Assert.Null(HatcheryEffectParts.TierOf("ei_depot_3"));
    }

    [Fact]
    public void Tiers_ListsDistinctTiers()
    {
        var tiers = HatcheryEffectParts.Tiers(Stems);
        Assert.Contains("universe", tiers);
        Assert.Contains("darkmatter", tiers);
        Assert.Contains("ai", tiers);
        Assert.Contains("vision", tiers);
        Assert.DoesNotContain("depot", tiers);
        // a floating-part stem must not create a phantom tier (e.g. "universe_bolt").
        Assert.DoesNotContain(tiers, t => t.Contains("bolt") || t.Contains("ring") || t.Contains("top"));
    }
}

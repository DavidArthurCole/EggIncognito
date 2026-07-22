namespace EggIncognito.GameData.Tests;

public sealed class BoostStackingTests {
    private static readonly IGameDataProvider Provider = GameDataProvider.CreateDefault();

    [Fact]
    public void Two_beacons_stack_additively_to_six_not_seven() {
        var effective = Provider.Effective(EffectTarget.BeaconMult, 1, new Dictionary<string, int> {
            ["boost_beacon_blue"] = 1,
            ["boost_beacon_blue_big"] = 1
        });
        Assert.Equal(6, effective);
    }

    [Fact]
    public void Single_beacon_gives_its_multiplier() {
        var effective = Provider.Effective(EffectTarget.BeaconMult, 1, new Dictionary<string, int> {
            ["boost_beacon_orange"] = 1
        });
        Assert.Equal(50, effective);
    }

    [Fact]
    public void Beacon_stored_multiplier_matches_magnitude_plus_one() {
        var beacon = Provider.Resolve("boost", "boost_beacon_purple")!;
        Assert.Equal(beacon.MetaDouble("multiplier"), beacon.Magnitude + 1);
    }

    [Fact]
    public void Beacon_applies_only_to_feed_prism_soulbeacon() {
        var beacon = Provider.Resolve("boost", "boost_beacon_blue")!;
        Assert.Equal("Feed,Prism,SoulBeacon", beacon.MetaString("appliesTo"));
    }

    [Fact]
    public void Prisms_multiply_ihr_per_row() {
        Assert.Equal(1000, Provider.Resolve("boost", "tachyon_prism_orange")!.Magnitude);
        Assert.Equal(CombineMode.Mul, Provider.Resolve("boost", "tachyon_prism_orange")!.CombineMode);
    }
}

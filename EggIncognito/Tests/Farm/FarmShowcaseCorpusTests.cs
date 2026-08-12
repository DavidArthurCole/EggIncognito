using EggIncognito.Core.Services.Farm;
using Ei;
using AssetType = Ei.ShellSpec.Types.AssetType;
using FarmElement = Ei.ShellDB.Types.FarmElement;

namespace EggIncognito.Tests.Farm;

public class FarmShowcaseCorpusTests {
    private static readonly Lazy<FarmShowcase.Result> Showcase = new(Load);
    private static readonly FarmPlacementData Data = FarmPlacementDataFixture.Build();

    private static FarmShowcase.Result Load() =>
        FarmShowcase.Parse(File.ReadAllText(Path.Combine(CaptureSessionManagerTests.RealContentRoot(),
            "Endpoints", "default", "ei", "get_shell_showcase.json")));

    [Fact]
    public void Showcase_DeduplicatesTheThreeBucketsById() {
        var result = Showcase.Value;
        Assert.True(result.Ok, result.Diagnostics);
        Assert.Equal(141, result.Presets.Count);
        Assert.Equal(result.Presets.Count, result.Presets.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.Presets, p => Assert.NotEmpty(p.Config.ShellConfigs));
    }

    [Fact]
    public void EveryFarm_PlacesEachSlotExactlyOnce() {
        foreach (var preset in Showcase.Value.Presets) {
            var state = FarmStateBuilder.FromConfiguration(preset.Config);
            var keys = FarmPlacementEngine.Place(state, Data).Placements
                .Where(p => p.AssetType is not null)
                .Select(p => (p.AssetType, p.Index))
                .ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void EveryHabSlotInAConfig_ReceivesAPlacement() {
        foreach (var preset in Showcase.Value.Presets) {
            var state = FarmStateBuilder.FromConfiguration(preset.Config);
            var placed = FarmPlacementEngine.Place(state, Data).Placements
                .Where(p => p.Element == FarmElement.HenHouse)
                .Select(p => p.Index)
                .ToHashSet();

            foreach (var c in preset.Config.ShellConfigs) {
                if (FarmAssetCatalog.ElementOf(c.AssetType) != FarmElement.HenHouse) continue;
                Assert.Contains((int)c.Index, placed);
            }
        }
    }

    [Fact]
    public void EverySiloSlotInAConfig_ReceivesAPlacement() {
        foreach (var preset in Showcase.Value.Presets) {
            var state = FarmStateBuilder.FromConfiguration(preset.Config);
            var placed = FarmPlacementEngine.Place(state, Data).Placements
                .Where(p => p.Element == FarmElement.Silo)
                .Select(p => p.Index)
                .ToHashSet();

            foreach (var c in preset.Config.ShellConfigs) {
                if (!FarmStateBuilder.IsSilo(c.AssetType)) continue;
                Assert.Contains((int)c.Index, placed);
            }
        }
    }

    [Fact]
    public void EveryFarm_KeepsHabsOnTheRowAndSilosOnTheirGrid() {
        foreach (var preset in Showcase.Value.Presets) {
            var state = FarmStateBuilder.FromConfiguration(preset.Config);
            foreach (var p in FarmPlacementEngine.Place(state, Data).Placements) {
                if (p.Element == FarmElement.HenHouse) {
                    Assert.Equal(Data.HabRowZ, p.Pos.Z, 4);
                    Assert.Equal(0f, p.Pos.Y);
                } else if (p.Element == FarmElement.Silo) {
                    Assert.Equal(FarmPlacementEngine.SiloPosition(Data, p.Index), p.Pos);
                }
            }
        }
    }

    [Fact]
    public void EveryFarm_ProducesFiniteCoordinates() {
        foreach (var preset in Showcase.Value.Presets) {
            var state = FarmStateBuilder.FromConfiguration(preset.Config);
            foreach (var p in FarmPlacementEngine.Place(state, Data).Placements) {
                Assert.True(float.IsFinite(p.Pos.X) && float.IsFinite(p.Pos.Y) && float.IsFinite(p.Pos.Z),
                    $"{preset.Id} {p.Element}:{p.Index}");
                Assert.True(p.Scale > 0f);
            }
        }
    }

    [Fact]
    public void StateBuilder_NeverInventsAnOutOfRangeTier() {
        foreach (var preset in Showcase.Value.Presets) {
            var state = FarmStateBuilder.FromConfiguration(preset.Config);
            Assert.Equal(FarmState.HabSlots, state.Habs.Count);
            Assert.All(state.Habs, t => Assert.InRange(t, 0, FarmState.EmptyHabTier));
            Assert.InRange(state.SilosOwned, 0, FarmState.MaxSilos);
            Assert.InRange(state.LabTier, 0, Data.LabExtents.Count - 1);
            Assert.InRange(state.DepotTier, 0, Data.DepotExtents.Count - 1);
            Assert.InRange(state.MissionControlLevel, 0, Data.MissionControlPose.Count - 1);
        }
    }

    [Fact]
    public void StateBuilder_ReadsHabTiersOffTheAssetType() {
        var config = new ShellDB.Types.FarmConfiguration();
        config.ShellConfigs.Add(new ShellDB.Types.ShellConfiguration {
            AssetType = AssetType.ChickenUniverse, Index = 0, ShellIdentifier = "x"
        });
        config.ShellConfigs.Add(new ShellDB.Types.ShellConfiguration {
            AssetType = AssetType.Coop, Index = 2, ShellIdentifier = "y"
        });

        var state = FarmStateBuilder.FromConfiguration(config);
        Assert.Equal(18, state.Habs[0]);
        Assert.Equal(FarmState.EmptyHabTier, state.Habs[1]);
        Assert.Equal(0, state.Habs[2]);
        Assert.Equal(FarmState.EmptyHabTier, state.Habs[3]);
    }

    [Fact]
    public void StateBuilder_ReadsSiloCountAndTierOffTheAssetType() {
        var config = new ShellDB.Types.FarmConfiguration();
        for (uint i = 0; i < 6; i++) {
            config.ShellConfigs.Add(new ShellDB.Types.ShellConfiguration {
                AssetType = AssetType.Silo1Large, Index = i, ShellIdentifier = "s"
            });
        }

        var state = FarmStateBuilder.FromConfiguration(config);
        Assert.Equal(6, state.SilosOwned);
        Assert.Equal(AssetType.Silo1Large, state.SiloAssetType);
    }

    [Fact]
    public void StateBuilder_ReadsBuildingTiersOffTheAssetType() {
        var config = new ShellDB.Types.FarmConfiguration();
        foreach (var t in (AssetType[])[AssetType.Depot7, AssetType.Lab6, AssetType.Hoa3,
                     AssetType.MissionControl3, AssetType.FuelTank4, AssetType.HatcheryDarkMatter]) {
            config.ShellConfigs.Add(new ShellDB.Types.ShellConfiguration {
                AssetType = t, Index = 0, ShellIdentifier = "z"
            });
        }

        var state = FarmStateBuilder.FromConfiguration(config);
        Assert.Equal(6, state.DepotTier);
        Assert.Equal(5, state.LabTier);
        Assert.Equal(2, state.HoaTier);
        Assert.Equal(2, state.MissionControlLevel);
        Assert.Equal(3, state.FuelTankTier);
        Assert.Equal(AssetType.HatcheryDarkMatter, state.HatcheryAssetType);
        Assert.Equal(Egg.DarkMatter, state.EggType);
    }

    [Fact]
    public void SubPieceRows_DoNotOverwriteThePrimaryHatchery() {
        var config = new ShellDB.Types.FarmConfiguration();
        config.ShellConfigs.Add(new ShellDB.Types.ShellConfiguration {
            AssetType = AssetType.HatcheryDarkMatter, Index = 0, ShellIdentifier = "a"
        });
        config.ShellConfigs.Add(new ShellDB.Types.ShellConfiguration {
            AssetType = AssetType.HatcheryDarkMatterRing1, Index = 0, ShellIdentifier = "a"
        });

        var state = FarmStateBuilder.FromConfiguration(config);
        Assert.Equal(AssetType.HatcheryDarkMatter, state.HatcheryAssetType);
        Assert.True(FarmStateBuilder.IsHatcheryPiece(AssetType.HatcheryDarkMatterRing1));
        Assert.False(FarmStateBuilder.IsPrimaryHatchery(AssetType.HatcheryDarkMatterRing1));
    }
}

using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class ZoneLayoutTests
{
    [Fact]
    public void Zones_CoversAllNineSlots()
    {
        Assert.Equal(9, ZoneLayout.Zones.Count);
        foreach (ZoneLayout.ZoneId id in Enum.GetValues<ZoneLayout.ZoneId>())
            Assert.True(ZoneLayout.Zones.ContainsKey(id), $"missing zone {id}");
    }

    [Fact]
    public void Resolve_PlacesEachSingleZoneAtItsAnchor()
    {
        var placed = ZoneLayout.Resolve("ei_lab_3", "ei_afx_construction_site", "ei_hatchery_edible",
            "ei_mission_control_1", "ei_fuel_tank_2", "ei_depot_3");

        var lab = Assert.Single(placed, p => p.Stem == "ei_lab_3");
        var hoa = Assert.Single(placed, p => p.Stem == "ei_afx_construction_site");
        var hatchery = Assert.Single(placed, p => p.Stem == "ei_hatchery_edible");
        var mc = Assert.Single(placed, p => p.Stem == "ei_mission_control_1");
        var fuel = Assert.Single(placed, p => p.Stem == "ei_fuel_tank_2");
        var depot = Assert.Single(placed, p => p.Stem == "ei_depot_3");

        Assert.Equal(ZoneLayout.Zones[ZoneLayout.ZoneId.Lab].AnchorZ, lab.Pos[2] - ZoneLayout.Zones[ZoneLayout.ZoneId.Lab].Depth / 2f, 2);
        Assert.Equal(ZoneLayout.Zones[ZoneLayout.ZoneId.Depot].AnchorZ, depot.Pos[2] - ZoneLayout.Zones[ZoneLayout.ZoneId.Depot].Depth / 2f, 2);
        Assert.True(hoa.Pos[0] > lab.Pos[0], "hoa sits right of lab");
        Assert.True(mc.Pos[0] > hatchery.Pos[0], "mission control sits right of hatchery");
        Assert.True(fuel.Pos[0] > mc.Pos[0], "fuel sits right of mission control");
    }

    [Fact]
    public void Resolve_AllPlacedMarkRecenterTrue()
    {
        var placed = ZoneLayout.Resolve("ei_lab_3", "ei_afx_construction_site", "ei_hatchery_edible",
            "ei_mission_control_1", "ei_fuel_tank_2", "ei_depot_3");
        Assert.All(placed, p => Assert.True(p.Recenter));
    }
}

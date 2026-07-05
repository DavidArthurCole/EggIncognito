using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class ZoneLayoutTests
{
    [Fact]
    public void Zones_CoversAllFiveSlots()
    {
        Assert.Equal(5, ZoneLayout.Zones.Count);
        foreach (ZoneLayout.ZoneId id in Enum.GetValues<ZoneLayout.ZoneId>())
            Assert.True(ZoneLayout.Zones.ContainsKey(id), $"missing zone {id}");
    }

    [Fact]
    public void Resolve_PlacesEachBuildingAtItsRowAnchor()
    {
        var placed = ZoneLayout.Resolve("ei_lab_3", "ei_afx_construction_site", "ei_hatchery_edible",
            "ei_mission_control_1", "ei_fuel_tank_2", "ei_depot_3");

        var lab = Assert.Single(placed, p => p.Stem == "ei_lab_3");
        var hoa = Assert.Single(placed, p => p.Stem == "ei_afx_construction_site");
        var hatchery = Assert.Single(placed, p => p.Stem == "ei_hatchery_edible");
        var mc = Assert.Single(placed, p => p.Stem == "ei_mission_control_1");
        var fuel = Assert.Single(placed, p => p.Stem == "ei_fuel_tank_2");
        var depot = Assert.Single(placed, p => p.Stem == "ei_depot_3");

        // Lab + Hoa share BackRow (same initial anchor - repackZoneRow spaces them apart by real mesh width
        // after the batch add); Hatchery/MissionControl/Fuel share MidRow; Depot is alone in FrontRow.
        Assert.Equal(ZoneLayout.BackRowZ, lab.Pos[2], 2);
        Assert.Equal(ZoneLayout.BackRowZ, hoa.Pos[2], 2);
        Assert.Equal(lab.Pos[0], hoa.Pos[0], 2);
        Assert.Equal(ZoneLayout.MidRowZ, hatchery.Pos[2], 2);
        Assert.Equal(ZoneLayout.MidRowZ, mc.Pos[2], 2);
        Assert.Equal(ZoneLayout.MidRowZ, fuel.Pos[2], 2);
        Assert.Equal(ZoneLayout.FrontRowZ, depot.Pos[2], 2);
    }

    [Fact]
    public void Resolve_AllPlacedMarkRecenterTrue()
    {
        var placed = ZoneLayout.Resolve("ei_lab_3", "ei_afx_construction_site", "ei_hatchery_edible",
            "ei_mission_control_1", "ei_fuel_tank_2", "ei_depot_3");
        Assert.All(placed, p => Assert.True(p.Recenter));
    }

    [Fact]
    public void IsInsideAnyZone_CoreRowPoint_IsInside()
    {
        Assert.True(ZoneLayout.IsInsideAnyZone(10f, ZoneLayout.MidRowZ + 1f));
    }

    [Fact]
    public void IsInsideAnyZone_FarAway_IsOutside()
    {
        Assert.False(ZoneLayout.IsInsideAnyZone(500f, 500f));
    }
}

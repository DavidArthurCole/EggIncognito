using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class PlaygroundPathsTests
{
    [Fact]
    public void ChickenRun_StartsAtHatcheryDoor_EndsAtHab()
    {
        var run = PlaygroundPaths.ChickenRun([0, 0, 2], [10, 0, -10]);
        Assert.True(run.Length >= 2);
       
        Assert.Equal(PlaygroundPaths.HatcheryDoorOffsetX, run[0][0], 3);
        Assert.Equal(2f, run[0][2], 3);
        var last = run[^1];
        Assert.Equal(10f, last[0], 3);
        Assert.Equal(-10f, last[2], 3);
    }

    [Fact]
    public void ChickenRun_LaneOffset_ShiftsZ()
    {
        var a = PlaygroundPaths.ChickenRun([0, 0, 2], [10, 0, -10], laneOffsetZ: 0f);
        var b = PlaygroundPaths.ChickenRun([0, 0, 2], [10, 0, -10], laneOffsetZ: 1.5f);
        Assert.Equal(a[0][2] + 1.5f, b[0][2], 3);
        Assert.Equal(a[^1][2] + 1.5f, b[^1][2], 3);
    }

    [Fact]
    public void RoadPath_RunsAlongX_AtRoadZ()
    {
        var road = PlaygroundPaths.RoadPath(-20, 20);
        Assert.Equal(2, road.Length);
        Assert.Equal(-20f, road[0][0], 3);
        Assert.Equal(20f, road[1][0], 3);
        Assert.Equal(PlaygroundPaths.RoadZ, road[0][2], 3);
        Assert.Equal(PlaygroundPaths.RoadZ, road[1][2], 3);
    }

    [Fact]
    public void RoadPath_DefaultsSpan_WhenSparse()
    {
       
        var road = PlaygroundPaths.RoadPath(5, 5);
        Assert.Equal(20f, road[0][0], 3);
        Assert.Equal(-20f, road[1][0], 3);
    }

    [Fact]
    public void RoadPath_PreservesDirection_WhenReversed()
    {
        var road = PlaygroundPaths.RoadPath(20, -20);
        Assert.Equal(20f, road[0][0], 3);
        Assert.Equal(-20f, road[1][0], 3);
    }

    [Fact]
    public void LaunchPath_IsVertical()
    {
        var launch = PlaygroundPaths.LaunchPath([16, 0, 9], 12);
        Assert.Equal(2, launch.Length);
        Assert.Equal(16f, launch[0][0], 3);
        Assert.Equal(9f, launch[0][2], 3);
        Assert.Equal(0f, launch[0][1], 3);
        Assert.Equal(12f, launch[1][1], 3);
        Assert.Equal(16f, launch[1][0], 3);
        Assert.Equal(9f, launch[1][2], 3);
    }
}

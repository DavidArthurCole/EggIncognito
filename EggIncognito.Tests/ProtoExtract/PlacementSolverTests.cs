using EggIncognito.Services.ProtoExtract;
using Xunit;
using static EggIncognito.Services.ProtoExtract.PlacementSolver;

namespace EggIncognito.Tests.ProtoExtract;

// PlacementSolver corrects a dragged element transform: grid snap + floor clamp + footprint overlap push. Pure
// math, no renderer, so every rule is exercised directly here.
public class PlacementSolverTests
{
    // a 2x2 unit footprint centered on the local origin.
    private static Box2 Unit2() => new(-1, 1, -1, 1);

    private static SolveRequest Req(float[] pos, Box2[]? others = null, float grid = 0, float localMinY = -1,
        float[]? rot = null, float scale = 1, bool clampFloor = true) =>
        new(pos, rot ?? [0, 0, 0], scale, Unit2(), localMinY, others ?? [], grid, clampFloor);

    [Fact]
    public void GridSnap_RoundsXZToNearestMultiple()
    {
        var r = Solve(Req([1.2f, 0, 2.9f], grid: 1f, localMinY: 0));
        Assert.Equal(1f, r.Pos[0], 3);
        Assert.Equal(3f, r.Pos[2], 3);
        Assert.True(r.Adjusted);
    }

    [Fact]
    public void GridSnap_Disabled_LeavesXZ()
    {
        var r = Solve(Req([1.2f, 0, 2.9f], grid: 0, localMinY: 0));
        Assert.Equal(1.2f, r.Pos[0], 3);
        Assert.Equal(2.9f, r.Pos[2], 3);
    }

    [Fact]
    public void FloorClamp_RaisesBuildingOutOfFloor()
    {
        // building lowest point is 1 below its origin; dropped at y=0 it sinks to -1. Clamp lifts origin to y=1.
        var r = Solve(Req([0, 0, 0], localMinY: -1));
        Assert.Equal(1f, r.Pos[1], 3);
        Assert.True(r.Adjusted);
    }

    [Fact]
    public void FloorClamp_DropsFloatingBuildingDown()
    {
        // origin floating at y=5, lowest point at origin (minY 0): clamp drops it so it rests on y=0.
        var r = Solve(Req([0, 5, 0], localMinY: 0));
        Assert.Equal(0f, r.Pos[1], 3);
    }

    [Fact]
    public void FloorClamp_Scaled_AccountsForScale()
    {
        // minY -1 at scale 2 = world lowest -2; origin must rise to y=2.
        var r = Solve(Req([0, 0, 0], localMinY: -1, scale: 2));
        Assert.Equal(2f, r.Pos[1], 3);
    }

    [Fact]
    public void FloorClamp_Disabled_KeepsAuthoredY()
    {
        var r = Solve(Req([0, 7.5f, 0], localMinY: -1, clampFloor: false));
        Assert.Equal(7.5f, r.Pos[1], 3);
    }

    [Fact]
    public void Overlap_PushesOutAlongShallowAxis()
    {
        // other occupies x[0..2], z[-1..1]. Our 2x2 at (1,0) overlaps; shallowest exit is -X (penetration 1).
        var other = new Box2(0, 2, -1, 1);
        var r = Solve(Req([1, 0, 0], others: [other], localMinY: 0));
        var foot = WorldFootprint(Unit2(), r.Pos[0], r.Pos[2], 0, 1);
        Assert.False(foot.Intersects(other));
        Assert.True(r.Adjusted);
    }

    [Fact]
    public void Overlap_None_LeavesPosition()
    {
        var other = new Box2(10, 12, 10, 12);
        var r = Solve(Req([0, 0, 0], others: [other], localMinY: 0));
        Assert.Equal(0f, r.Pos[0], 3);
        Assert.Equal(0f, r.Pos[2], 3);
    }

    [Fact]
    public void Overlap_MultipleOthers_ResolvesAll()
    {
        // two neighbors hemming the element; the solver must iterate until clear of both.
        var a = new Box2(0.5f, 2.5f, -1, 1);
        var b = new Box2(-2.5f, -0.5f, -1, 1);
        var r = Solve(Req([0, 0, 0], others: [a, b], localMinY: 0));
        var foot = WorldFootprint(Unit2(), r.Pos[0], r.Pos[2], 0, 1);
        Assert.False(foot.Intersects(a));
        Assert.False(foot.Intersects(b));
    }

    [Fact]
    public void Overlap_WithGrid_StaysOnGrid()
    {
        var other = new Box2(0, 2, -1, 1);
        var r = Solve(Req([1, 0, 0], others: [other], grid: 1f, localMinY: 0));
        Assert.Equal(r.Pos[0], MathF.Round(r.Pos[0]), 3); // landed on a grid multiple
    }

    [Fact]
    public void WorldFootprint_YawWidensTheBox()
    {
        // a 4-wide, 2-deep box rotated 90deg should swap extents (width<->depth).
        var local = new Box2(-2, 2, -1, 1);
        var f0 = WorldFootprint(local, 0, 0, 0, 1);
        var f90 = WorldFootprint(local, 0, 0, 90, 1);
        Assert.Equal(4f, f0.Width, 2);
        Assert.Equal(2f, f0.Depth, 2);
        Assert.Equal(2f, f90.Width, 2);
        Assert.Equal(4f, f90.Depth, 2);
    }

    [Fact]
    public void WorldFootprint_45Deg_WidensBeyondAxisAligned()
    {
        var local = new Box2(-2, 2, -1, 1);
        var f45 = WorldFootprint(local, 0, 0, 45, 1);
        Assert.True(f45.Width > 4f); // rotated diagonal extends past the axis-aligned width
    }

    [Fact]
    public void Solve_InvalidPos_ReturnsUnchanged()
    {
        var r = Solve(new SolveRequest([0, 0], [0, 0, 0], 1, Unit2(), 0, [], 0));
        Assert.False(r.Adjusted);
        Assert.Equal("invalid request", r.Reason);
    }
}

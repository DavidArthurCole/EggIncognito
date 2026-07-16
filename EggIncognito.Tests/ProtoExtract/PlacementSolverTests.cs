using System.Collections.Generic;
using System.Linq;
using EggIncognito.Services.ProtoExtract;
using Xunit;
using static EggIncognito.Services.ProtoExtract.PlacementSolver;

namespace EggIncognito.Tests.ProtoExtract;

public class PlacementSolverTests
{
   
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
       
        var r = Solve(Req([0, 0, 0], localMinY: -1));
        Assert.Equal(1f, r.Pos[1], 3);
        Assert.True(r.Adjusted);
    }

    [Fact]
    public void FloorClamp_DropsFloatingBuildingDown()
    {
       
        var r = Solve(Req([0, 5, 0], localMinY: 0));
        Assert.Equal(0f, r.Pos[1], 3);
    }

    [Fact]
    public void FloorClamp_Scaled_AccountsForScale()
    {
       
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
    public void Solve_DoesNotPushOnOverlap_NoFling()
    {
       
        var other = new Box2(0, 2, -1, 1);
        var r = Solve(Req([1, 0, 0], others: [other], localMinY: 0));
        Assert.Equal(1f, r.Pos[0], 3);
        Assert.Equal(0f, r.Pos[2], 3);
        Assert.Equal("overlap", r.Reason);
    }

    [Fact]
    public void Overlap_None_NoOverlapReason()
    {
        var other = new Box2(10, 12, 10, 12);
        var r = Solve(Req([0, 0, 0], others: [other], localMinY: 0));
        Assert.Equal(0f, r.Pos[0], 3);
        Assert.Equal("ok", r.Reason);
    }

    [Fact]
    public void WorldFootprint_YawWidensTheBox()
    {
       
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
        Assert.True(f45.Width > 4f);
    }

    [Fact]
    public void Solve_InvalidPos_ReturnsUnchanged()
    {
        var r = Solve(new SolveRequest([0, 0], [0, 0, 0], 1, Unit2(), 0, [], 0));
        Assert.False(r.Adjusted);
        Assert.Equal("invalid request", r.Reason);
    }

    [Fact]
    public void SnapToGrid_UnitElement_OccupiesOneCell()
    {
       
        var r = SnapToGrid(new Box2(-0.4f, 0.4f, -0.4f, 0.4f), 1, 0.4f, 0.4f, 1f, new HashSet<Cell>());
        Assert.Single(r.Cells);
        Assert.Equal(new Cell(0, 0), r.Cells[0]);
        Assert.True(r.Valid);
        Assert.Equal(0.5f, r.CenterX, 3);
        Assert.Equal(0.5f, r.CenterZ, 3);
    }

    [Fact]
    public void SnapToGrid_BigElement_SpansMultipleCells()
    {
       
        var r = SnapToGrid(new Box2(-1.5f, 1.5f, -0.5f, 0.5f), 1, 5f, 5f, 1f, new HashSet<Cell>());
        Assert.Equal(3, r.Cells.Count);
        Assert.True(r.Valid);
    }

    [Fact]
    public void SnapToGrid_OccupiedCell_IsInvalid()
    {
        var occupied = new HashSet<Cell> { new(0, 0) };
        var r = SnapToGrid(new Box2(-0.4f, 0.4f, -0.4f, 0.4f), 1, 0.3f, 0.3f, 1f, occupied);
        Assert.False(r.Valid);
    }

    [Fact]
    public void SnapToGrid_FreeNeighborOfOccupied_IsValid()
    {
        var occupied = new HashSet<Cell> { new(0, 0) };
        var r = SnapToGrid(new Box2(-0.4f, 0.4f, -0.4f, 0.4f), 1, 1.5f, 0.3f, 1f, occupied);
        Assert.True(r.Valid);
        Assert.Equal(new Cell(1, 0), r.Cells[0]);
    }

    [Fact]
    public void SnapToGrid_NoFling_InvalidStaysAtTarget()
    {
       
        var occupied = new HashSet<Cell> { new(0, 0) };
        var r = SnapToGrid(new Box2(-0.4f, 0.4f, -0.4f, 0.4f), 1, 0.1f, 0.1f, 1f, occupied);
        Assert.Equal(0.5f, r.CenterX, 3);
        Assert.False(r.Valid);
    }

    [Fact]
    public void CellsOf_RoundTripsWithSnap()
    {
        var foot = new Box2(-1.5f, 1.5f, -0.5f, 0.5f);
        var snap = SnapToGrid(foot, 1, 5f, 5f, 1f, new HashSet<Cell>());
        var cells = CellsOf(foot, 1, snap.CenterX, snap.CenterZ, 1f).ToHashSet();
        Assert.Equal(snap.Cells.ToHashSet(), cells);
    }

    [Fact]
    public void SnapToGrid_ZeroCell_NoOp()
    {
        var r = SnapToGrid(Unit2(), 1, 3.7f, 2.2f, 0f, new HashSet<Cell>());
        Assert.True(r.Valid);
        Assert.Empty(r.Cells);
    }

    [Fact]
    public void Domino_GrownElement_PushesAdjacentNeighbor()
    {
       
        var changed = new GridBox("a", 0, 0, 2, 1);
        var nb = new GridBox("b", 1, 0, 1, 1);
        var moves = DominoNudge(changed, [nb]);
        var m = Assert.Single(moves);
        Assert.Equal("b", m.Id);
        Assert.Equal(1, m.DeltaCol);
        Assert.Equal(0, m.DeltaRow);
    }

    [Fact]
    public void Domino_Cascades_ThroughAChain()
    {
       
        var changed = new GridBox("a", 0, 0, 2, 1);
        var b = new GridBox("b", 1, 0, 1, 1);
        var c = new GridBox("c", 2, 0, 1, 1);
        var moves = DominoNudge(changed, [b, c]).ToDictionary(m => m.Id, m => m.DeltaCol);
        Assert.Equal(1, moves["b"]);
        Assert.Equal(1, moves["c"]);
    }

    [Fact]
    public void Domino_NoOverlap_NoMoves()
    {
        var changed = new GridBox("a", 0, 0, 1, 1);
        var far = new GridBox("b", 5, 5, 1, 1);
        Assert.Empty(DominoNudge(changed, [far]));
    }

    [Fact]
    public void Domino_PushesLeftNeighborLeft()
    {
       
        var changed = new GridBox("a", 2, 0, 2, 1);
        var left = new GridBox("b", 1, 0, 2, 1);   
        var m = Assert.Single(DominoNudge(changed, [left]));
        Assert.True(m.DeltaCol < 0);
    }

    [Fact]
    public void ZoneLocked_InsideAZone_NoReasonChange()
    {
       
        var r = Solve(new SolveRequest([7f, 0, 12.5f], [0, 0, 0], 1, Unit2(), 0, [], 0, ZoneLocked: true));
        Assert.Equal("ok", r.Reason);
    }

    [Fact]
    public void ZoneLocked_OutsideAllZones_FlagsOutsideZone()
    {
        var r = Solve(new SolveRequest([500f, 0, 500f], [0, 0, 0], 1, Unit2(), 0, [], 0, ZoneLocked: true));
        Assert.Equal("outside-zone", r.Reason);
    }

    [Fact]
    public void ZoneLocked_False_IgnoresZones()
    {
        var r = Solve(new SolveRequest([500f, 0, 500f], [0, 0, 0], 1, Unit2(), 0, [], 0, ZoneLocked: false));
        Assert.Equal("ok", r.Reason);
    }
}

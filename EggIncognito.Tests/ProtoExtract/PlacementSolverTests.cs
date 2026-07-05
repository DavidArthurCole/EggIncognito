using System.Collections.Generic;
using System.Linq;
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
    public void Solve_DoesNotPushOnOverlap_NoFling()
    {
        // Solve no longer relocates an overlapping piece (that flung self-placing meshes across the scene). It
        // leaves the position put and only flags the overlap; the block-grid path owns no-overlap now.
        var other = new Box2(0, 2, -1, 1);
        var r = Solve(Req([1, 0, 0], others: [other], localMinY: 0));
        Assert.Equal(1f, r.Pos[0], 3); // NOT pushed away
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

    [Fact]
    public void SnapToGrid_UnitElement_OccupiesOneCell()
    {
        // a sub-cell element (1 wide) at (0.4, 0.4) snaps into cell (0,0).
        var r = SnapToGrid(new Box2(-0.4f, 0.4f, -0.4f, 0.4f), 1, 0.4f, 0.4f, 1f, new HashSet<Cell>());
        Assert.Single(r.Cells);
        Assert.Equal(new Cell(0, 0), r.Cells[0]);
        Assert.True(r.Valid);
        Assert.Equal(0.5f, r.CenterX, 3); // odd 1-span centers in the cell
        Assert.Equal(0.5f, r.CenterZ, 3);
    }

    [Fact]
    public void SnapToGrid_BigElement_SpansMultipleCells()
    {
        // a 3-wide x 1-deep element at cell size 1 occupies 3x1 cells.
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
        // the old solver flung an overlapping piece far away. The block grid leaves the center at the snapped
        // target cell (just flags it invalid); the caller reverts, never relocates to the edge.
        var occupied = new HashSet<Cell> { new(0, 0) };
        var r = SnapToGrid(new Box2(-0.4f, 0.4f, -0.4f, 0.4f), 1, 0.1f, 0.1f, 1f, occupied);
        Assert.Equal(0.5f, r.CenterX, 3); // snapped to cell (0,0) center, NOT pushed to some far cell
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
        // changed grew to 2 wide at cols 0..1; a neighbor sits at col 1 (now overlapped). Push it to col 2.
        var changed = new GridBox("a", 0, 0, 2, 1);
        var nb = new GridBox("b", 1, 0, 1, 1);
        var moves = DominoNudge(changed, [nb]);
        var m = Assert.Single(moves);
        Assert.Equal("b", m.Id);
        Assert.Equal(1, m.DeltaCol); // shoved one cell right to clear
        Assert.Equal(0, m.DeltaRow);
    }

    [Fact]
    public void Domino_Cascades_ThroughAChain()
    {
        // changed at col 0..1 overlaps b at col1; b shoves into c at col2; the cascade moves both.
        var changed = new GridBox("a", 0, 0, 2, 1);
        var b = new GridBox("b", 1, 0, 1, 1);
        var c = new GridBox("c", 2, 0, 1, 1);
        var moves = DominoNudge(changed, [b, c]).ToDictionary(m => m.Id, m => m.DeltaCol);
        Assert.Equal(1, moves["b"]); // b -> col2
        Assert.Equal(1, moves["c"]); // c shoved -> col3
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
        // a neighbor whose center is LEFT of the grown element gets pushed left (away), not right through it.
        var changed = new GridBox("a", 2, 0, 2, 1); // cols 2..3
        var left = new GridBox("b", 1, 0, 2, 1);    // cols 1..2, center left of changed
        var m = Assert.Single(DominoNudge(changed, [left]));
        Assert.True(m.DeltaCol < 0); // shoved further left
    }

    [Fact]
    public void ZoneLocked_InsideAZone_NoReasonChange()
    {
        // Depot zone anchor (2,10) width 10 depth 5 -> center (7,12.5) is inside it.
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

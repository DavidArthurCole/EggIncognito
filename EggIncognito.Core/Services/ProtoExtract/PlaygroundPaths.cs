namespace EggIncognito.Services.ProtoExtract;

// Derives world-space waypoint paths for the playground's animated actors from the placed elements' positions,
// so motion follows the user's layout. A waypoint is float[]{x,y,z}.
//
// STOPGAP (see CLAUDE.md "EXTRACT, don't author"): hand-authored approximations of the game's real actor
// motion. Extract the real paths from FarmScene / model data and replace this. Tracked in
// docs/superpowers/specs/2026-06-28-playground-building-animations-design.md.
public static class PlaygroundPaths
{
    // The road runs along X between the depot and the hyperloop. Vehicles drive here; the hyperloop itself is
    // not the road.
    public const float RoadZ = 15f;

    // Approximated offset from the hatchery placement toward +X for the chicken-emergence "door".
    // STOPGAP: the exact door point is in the hatchery model node graph; extract it.
    public const float HatcheryDoorOffsetX = 2.5f;

    // A chicken's run: from the hatchery door out to the hab ramp, with a gentle midpoint bow. laneOffsetZ
    // shifts the whole run sideways so several chickens run in parallel lanes.
    public static float[][] ChickenRun(float[] hatcheryPos, float[] habPos, float laneOffsetZ = 0f)
    {
        var start = new[] { hatcheryPos[0] + HatcheryDoorOffsetX, 0f, hatcheryPos[2] + laneOffsetZ };
        var end = new[] { habPos[0], 0f, habPos[2] + laneOffsetZ };
        var mid = new[] { (start[0] + end[0]) / 2f + 1.5f, 0f, (start[2] + end[2]) / 2f };
        return [start, mid, end];
    }

    // The road as a straight drive line along X at the road Z, from the first arg to the second (pass them
    // reversed to drive the other way).
    public static float[][] RoadPath(float fromX, float toX)
    {
        if (System.MathF.Abs(toX - fromX) < 1f) { fromX = 20f; toX = -20f; }
        return [[fromX, 0f, RoadZ], [toX, 0f, RoadZ]];
    }

    // A vertical launch line from the mission-control pad straight up by height.
    public static float[][] LaunchPath(float[] missionControlPos, float height)
    {
        var x = missionControlPos[0];
        var z = missionControlPos[2];
        return [[x, 0f, z], [x, height, z]];
    }
}

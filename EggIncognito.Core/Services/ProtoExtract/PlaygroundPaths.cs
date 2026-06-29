namespace EggIncognito.Services.ProtoExtract;

// Derives world-space waypoint paths for the playground's animated actors from the placed elements' positions,
// so motion follows the user's layout. Pure: positions in, waypoints out. A waypoint is float[]{x,y,z}.
//
// STOPGAP (see CLAUDE.md "EXTRACT, don't author"): these are hand-authored approximations of the game's real
// actor motion. The game defines the actual chicken-run / vehicle-drive / rocket-launch paths in its binary
// (FarmScene + related compiled methods) or model data. Extract those and replace this. Tracked in
// docs/superpowers/specs/2026-06-28-playground-building-animations-design.md.
public static class PlaygroundPaths
{
    // The road runs along X between the depot (z~7-12) and the hyperloop (z~19-27). Vehicles drive here. The
    // hyperloop itself is NOT the road and its cars do not drive.
    public const float RoadZ = 15f;

    // A chicken's run: from the hatchery door out to the hab ramp, with a gentle midpoint bow so it curves
    // rather than sliding straight. The client Catmull-Rom smooths it further.
    public static float[][] ChickenRun(float[] hatcheryPos, float[] habPos)
    {
        var start = new[] { hatcheryPos[0], 0f, hatcheryPos[2] };
        var end = new[] { habPos[0], 0f, habPos[2] };
        var mid = new[] { (start[0] + end[0]) / 2f + 1.5f, 0f, (start[2] + end[2]) / 2f };
        return [start, mid, end];
    }

    // The road as a straight drive line along X at the road Z, from the first arg to the second (pass them
    // reversed to drive the other way). The endpoints come from the placed elements' X bounds, or a default
    // span when they are too close together.
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

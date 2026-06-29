namespace EggIncognito.Services.ProtoExtract;

// Derives world-space waypoint paths for the playground's animated actors from the placed elements' positions,
// so motion follows the user's layout. Pure: positions in, waypoints out. The game's real motion is hardcoded
// C++ and not extractable, so these are authored approximations. A waypoint is float[]{x,y,z}.
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

    // The road as a straight drive line along X at the road Z. minX/maxX come from the placed elements' X
    // bounds (or a default span when sparse).
    public static float[][] RoadPath(float minX, float maxX)
    {
        if (maxX - minX < 1f) { minX = -20f; maxX = 20f; }
        return [[minX, 0f, RoadZ], [maxX, 0f, RoadZ]];
    }

    // A vertical launch line from the mission-control pad straight up by height.
    public static float[][] LaunchPath(float[] missionControlPos, float height)
    {
        var x = missionControlPos[0];
        var z = missionControlPos[2];
        return [[x, 0f, z], [x, height, z]];
    }
}

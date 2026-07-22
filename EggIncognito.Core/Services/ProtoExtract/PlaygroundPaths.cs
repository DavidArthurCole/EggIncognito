namespace EggIncognito.Services.ProtoExtract;

//

public static class PlaygroundPaths {


    public const float RoadZ = 15f;



    public const float HatcheryDoorOffsetX = 2.5f;



    public static float[][] ChickenRun(float[] hatcheryPos, float[] habPos, float laneOffsetZ = 0f) {
        var start = new[] { hatcheryPos[0] + HatcheryDoorOffsetX, 0f, hatcheryPos[2] + laneOffsetZ };
        var end = new[] { habPos[0], 0f, habPos[2] + laneOffsetZ };
        var mid = new[] { (start[0] + end[0]) / 2f + 1.5f, 0f, (start[2] + end[2]) / 2f };
        return [start, mid, end];
    }



    public static float[][] RoadPath(float fromX, float toX) {
        if (System.MathF.Abs(toX - fromX) < 1f) { fromX = 20f; toX = -20f; }
        return [[fromX, 0f, RoadZ], [toX, 0f, RoadZ]];
    }


    public static float[][] LaunchPath(float[] missionControlPos, float height) {
        var x = missionControlPos[0];
        var z = missionControlPos[2];
        return [[x, 0f, z], [x, height, z]];
    }
}

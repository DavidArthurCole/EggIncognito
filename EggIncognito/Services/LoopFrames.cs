namespace EggIncognito.Services;


public static class LoopFrames
{
    public static int Count(int fps, double periodSeconds)
    {
        var n = (int)Math.Round(fps * periodSeconds, MidpointRounding.AwayFromZero);
        return Math.Max(1, n);
    }

    public static int DelayMs(double periodSeconds, int frameCount)
    {
        if (frameCount <= 0) return 1;
        var ms = (int)Math.Round(periodSeconds / frameCount * 1000.0, MidpointRounding.AwayFromZero);
        return Math.Max(1, ms);
    }
}

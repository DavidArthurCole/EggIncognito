namespace EggIncognito.Services;

// Pure loop-frame math for the playground GIF export, matching the recorder JS's rule: a perfect loop
// captures N = round(fps*period) frames evenly over one animation period, each delayed period/N.
// Guards keep N and delay at least 1 so a degenerate input never divides by zero.
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

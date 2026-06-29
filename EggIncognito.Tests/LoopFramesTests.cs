using EggIncognito.Services;

namespace EggIncognito.Tests;

public class LoopFramesTests
{
    [Fact]
    public void Count_IsFpsTimesPeriodRounded()
    {
        Assert.Equal(120, LoopFrames.Count(20, 6.0));
        Assert.Equal(90, LoopFrames.Count(15, 6.0));
        Assert.Equal(1, LoopFrames.Count(20, 0.0)); // degenerate period clamps to one frame
    }

    [Fact]
    public void DelayMs_DividesPeriodAcrossFrames()
    {
        Assert.Equal(50, LoopFrames.DelayMs(6.0, 120)); // 6000ms / 120 = 50ms
        Assert.Equal(67, LoopFrames.DelayMs(6.0, 90)); // 6000 / 90 = 66.67 -> 67
        Assert.Equal(1, LoopFrames.DelayMs(6.0, 0)); // guard divide-by-zero
    }
}

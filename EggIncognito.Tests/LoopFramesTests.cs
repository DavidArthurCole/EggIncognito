using EggIncognito.Services;

namespace EggIncognito.Tests;

public class LoopFramesTests
{
    [Fact]
    public void Count_IsFpsTimesPeriodRounded()
    {
        Assert.Equal(120, LoopFrames.Count(20, 6.0));
        Assert.Equal(90, LoopFrames.Count(15, 6.0));
        Assert.Equal(1, LoopFrames.Count(20, 0.0));
    }

    [Fact]
    public void DelayMs_DividesPeriodAcrossFrames()
    {
        Assert.Equal(50, LoopFrames.DelayMs(6.0, 120));
        Assert.Equal(67, LoopFrames.DelayMs(6.0, 90));
        Assert.Equal(1, LoopFrames.DelayMs(6.0, 0));
    }
}

using EggIncognito.Services;

namespace EggIncognito.Tests;

public class AutosaveRecencyTests {
    [Fact]
    public void IsFresh_TrueWithinWindow() {
        var now = 1_000_000_000_000L;
        Assert.True(AutosaveRecency.IsFresh(now - 5 * 60_000, now));
        Assert.True(AutosaveRecency.IsFresh(now - 29 * 60_000, now));
    }

    [Fact]
    public void IsFresh_FalseWhenStaleOrFuture() {
        var now = 1_000_000_000_000L;
        Assert.False(AutosaveRecency.IsFresh(now - 31 * 60_000, now));
        Assert.False(AutosaveRecency.IsFresh(now + 60_000, now));
        Assert.False(AutosaveRecency.IsFresh(0, now));
    }
}

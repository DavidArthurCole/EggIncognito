using EggIncognito.Services.Events;

namespace EggIncognito.Tests;

public class UnixSecondsTests {
    [Fact]
    public void RoundTrips_FractionalSeconds() {
        double seconds = 1609517284.8757882;
        var time = UnixSeconds.ToTime(seconds);
        Assert.Equal(seconds, UnixSeconds.FromTime(time), 3);
    }

    [Fact]
    public void ToTime_EpochIsZero() => Assert.Equal(DateTimeOffset.UnixEpoch, UnixSeconds.ToTime(0));

    [Fact]
    public void IsValid_AcceptsInRangeValue() => Assert.True(UnixSeconds.IsValid(1609517288.348589));

    [Fact]
    public void IsValid_RejectsNonFiniteAndOutOfRangeValues() {
        Assert.False(UnixSeconds.IsValid(double.NaN));
        Assert.False(UnixSeconds.IsValid(double.PositiveInfinity));
        Assert.False(UnixSeconds.IsValid(1e18));
        Assert.False(UnixSeconds.IsValid(-1e18));
    }
}

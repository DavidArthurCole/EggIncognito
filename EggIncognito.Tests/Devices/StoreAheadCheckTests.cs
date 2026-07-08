using EggIncognito.Services.Devices;
using Xunit;

namespace EggIncognito.Tests.Devices;

// Dotted-numeric semver compare, so 1.36 > 1.35.8.
public class StoreAheadCheckTests
{
    [Theory]
    [InlineData("1.36", "1.35.8", true)]
    [InlineData("1.36.0.2", "1.35.8", true)]
    [InlineData("1.35.8", "1.35.8", false)]
    [InlineData("1.35.7", "1.35.8", false)]
    [InlineData("1.36", "1.6", true)]
    public void IsAhead_ComparesSemver(string storeLatest, string installed, bool expected) =>
        Assert.Equal(expected, StoreAheadCheck.IsAhead(storeLatest, installed));

    [Theory]
    [InlineData(null, "1.35.8")]
    [InlineData("", "1.35.8")]
    [InlineData("1.36", null)]
    [InlineData("1.36", "")]
    public void IsAhead_FalseWhenEitherMissing(string? storeLatest, string? installed) =>
        Assert.False(StoreAheadCheck.IsAhead(storeLatest, installed));
}

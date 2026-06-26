using EggIncognito.Services.Devices;
using Xunit;

namespace EggIncognito.Tests.Devices;

// IsAhead is the shared gate for both the auto-path (RealDeviceUpgrader) and the manual admin Update
// endpoint: true only when the store's newest version is strictly greater than what is installed. Semver
// dotted-numeric compare (so 1.36 > 1.35.8). StoreLatestAsync is DB-backed and covered by integration.
public class StoreAheadCheckTests
{
    [Theory]
    [InlineData("1.36", "1.35.8", true)]
    [InlineData("1.36.0.2", "1.35.8", true)]
    [InlineData("1.35.8", "1.35.8", false)] // equal: not ahead
    [InlineData("1.35.7", "1.35.8", false)] // store behind: not ahead
    [InlineData("1.36", "1.6", true)] // numeric, not lexical (36 > 6)
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

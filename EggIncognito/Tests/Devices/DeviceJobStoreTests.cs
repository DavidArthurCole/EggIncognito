using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Tests.Devices;

public class DeviceJobStoreTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private static (DateTimeOffset, bool?, string?) P(int minutesAgo, bool reachable, string outcome) =>
        (Now.AddMinutes(-minutesAgo), reachable, outcome);

    [Fact]
    public void StatsCountsOnlyTheWindow() {
        var rows = new[] { P(5, true, "no_change"), P(3000, true, "no_change") };
        var s = DeviceJobStore.StatsFor("d1", rows, Now.AddHours(-24));
        Assert.Equal(1, s.Total);
        Assert.Equal(1, s.ReachableCount);
    }

    [Fact]
    public void ConsecutiveFailuresCountsFromNewestUntilASuccess() {
        var rows = new[] { P(1, false, "unreachable"), P(2, false, "unreachable"), P(3, true, "no_change") };
        var s = DeviceJobStore.StatsFor("d1", rows, Now.AddHours(-24));
        Assert.Equal(2, s.ConsecutiveFailures);
    }

    [Fact]
    public void ConsecutiveFailuresIsZeroWhenNewestIsReachable() {
        var rows = new[] { P(1, true, "no_change"), P(2, false, "unreachable") };
        var s = DeviceJobStore.StatsFor("d1", rows, Now.AddHours(-24));
        Assert.Equal(0, s.ConsecutiveFailures);
    }

    [Fact]
    public void LastSuccessAndLastFailureComeFromTheFullHistoryNotTheWindow() {
        var rows = new[] { P(1, false, "unreachable"), P(3000, true, "no_change") };
        var s = DeviceJobStore.StatsFor("d1", rows, Now.AddHours(-24));
        Assert.Equal(Now.AddMinutes(-3000), s.LastSuccessAt);
        Assert.Equal(Now.AddMinutes(-1), s.LastFailureAt);
    }

    [Fact]
    public void ResultCountsGroupByOutcomeWithinTheWindow() {
        var rows = new[] { P(1, true, "new_version"), P(2, true, "no_change"), P(3, true, "no_change") };
        var s = DeviceJobStore.StatsFor("d1", rows, Now.AddHours(-24));
        Assert.Equal(1, s.ResultCounts["new_version"]);
        Assert.Equal(2, s.ResultCounts["no_change"]);
    }

    [Fact]
    public void EmptyHistoryYieldsZeroes() {
        var s = DeviceJobStore.StatsFor("d1", [], Now.AddHours(-24));
        Assert.Equal(0, s.Total);
        Assert.Equal(0, s.ConsecutiveFailures);
        Assert.Null(s.LastSuccessAt);
    }

    [Fact]
    public void JobIsAbandonedOnlyAfterThirtyMinutes() {
        Assert.False(DeviceJobStore.IsAbandoned(Now.AddMinutes(-29), Now));
        Assert.True(DeviceJobStore.IsAbandoned(Now.AddMinutes(-31), Now));
    }

    [Theory]
    [InlineData("ok", DeviceJobStates.Succeeded)]
    [InlineData("updated", DeviceJobStates.Succeeded)]
    [InlineData("up_to_date", DeviceJobStates.Succeeded)]
    [InlineData("manual_needed", DeviceJobStates.Succeeded)]
    [InlineData("failed", DeviceJobStates.Failed)]
    [InlineData("partial", DeviceJobStates.Failed)]
    [InlineData("error", DeviceJobStates.Failed)]
    [InlineData("unreachable", DeviceJobStates.Failed)]
    [InlineData("unsupported", DeviceJobStates.Failed)]
    [InlineData("abandoned", DeviceJobStates.Failed)]
    public void OutcomeDecidesState(string outcome, string state) =>
        Assert.Equal(state, DeviceJobStore.StateFor(outcome));

    [Fact]
    public void EveryHarvestStatusThatSawAFailureIsAFailedState() {
        Assert.Equal(DeviceJobStates.Succeeded, DeviceJobStore.StateFor(HarvestStatus.Ok));
        Assert.Equal(DeviceJobStates.Failed, DeviceJobStore.StateFor(HarvestStatus.Partial));
        Assert.Equal(DeviceJobStates.Failed, DeviceJobStore.StateFor(HarvestStatus.Failed));
        Assert.Equal(DeviceJobStates.Failed, DeviceJobStore.StateFor(HarvestStatus.Unreachable));
    }

    [Fact]
    public void MissingOutcomeStaysSucceeded() =>
        Assert.Equal(DeviceJobStates.Succeeded, DeviceJobStore.StateFor(null));
}

using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class JobGroupCollapserTests {
    private static DateTimeOffset At(long id) => DateTimeOffset.UnixEpoch.AddMinutes(id);

    private static DeviceJobRow Row(long id, string? outcome = "no_change",
        string state = DeviceJobStates.Succeeded, string kind = DeviceJobKinds.Probe,
        string trigger = "poll", string? message = null) =>
        new(id, "dev", kind, state, trigger, At(id), At(id), outcome, message ?? $"msg{id}",
            true, $"1.{id}", $"{id}", null, $"rev{id}", null);

    [Fact]
    public void IdenticalProbesFoldIntoOneGroup() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(3), Row(2), Row(1)]);
        var page = c.Finish();

        var g = Assert.Single(page.Rows);
        Assert.Equal(3, g.Repeat);
        Assert.Equal(3L, g.Id);
        Assert.Equal(At(1), g.StartedAt);
        Assert.Equal(At(3), g.LastAt);
        Assert.Null(page.NextBefore);
    }

    [Fact]
    public void FoldedGroupReportsTheNewestRowFacts() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(3), Row(2), Row(1)]);

        var g = Assert.Single(c.Finish().Rows);
        Assert.Equal("msg3", g.Message);
        Assert.Equal("1.3", g.AppVersion);
        Assert.Equal("3", g.Build);
        Assert.Equal("rev3", g.Revision);
        Assert.Equal(At(3), g.FinishedAt);
    }

    [Fact]
    public void DifferingOutcomeBreaksTheRun() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(4), Row(3), Row(2, "updated"), Row(1)]);
        var rows = c.Finish().Rows;

        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows[0].Repeat);
        Assert.Equal(1, rows[1].Repeat);
        Assert.Equal("updated", rows[1].Outcome);
        Assert.Equal(1, rows[2].Repeat);
    }

    [Fact]
    public void DifferingTriggerBreaksTheRun() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(2), Row(1, trigger: "manual")]);

        Assert.Equal(2, c.Finish().Rows.Count);
    }

    [Fact]
    public void DifferingKindBreaksTheRun() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(2), Row(1, kind: DeviceJobKinds.StoreCheck)]);

        Assert.Equal(2, c.Finish().Rows.Count);
    }

    [Fact]
    public void RunningJobsNeverFold() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(2, null, DeviceJobStates.Running), Row(1, null, DeviceJobStates.Running)]);
        var rows = c.Finish().Rows;

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Repeat));
    }

    [Fact]
    public void ARunningJobSplitsAnOtherwiseIdenticalRun() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(3), Row(2, null, DeviceJobStates.Running), Row(1)]);
        var rows = c.Finish().Rows;

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Repeat));
    }

    [Fact]
    public void RunSplitAcrossBatchesStillFolds() {
        var c = new JobGroupCollapser(20);
        c.Feed([Row(4), Row(3)]);
        c.Feed([Row(2), Row(1)]);

        var g = Assert.Single(c.Finish().Rows);
        Assert.Equal(4, g.Repeat);
        Assert.Equal(At(1), g.StartedAt);
    }

    [Fact]
    public void LastPageHasNoNextBefore() {
        var c = new JobGroupCollapser(2);
        c.Feed([Row(2, "updated"), Row(1)]);
        var page = c.Finish();

        Assert.Equal(2, page.Rows.Count);
        Assert.Null(page.NextBefore);
    }

    [Fact]
    public void FullPageWithMoreRowsReportsNextBefore() {
        var c = new JobGroupCollapser(2);
        c.Feed([Row(3, "updated"), Row(2, "failed"), Row(1)]);
        var page = c.Finish();

        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(2L, page.NextBefore);
        Assert.True(c.Complete);
    }

    [Fact]
    public void NextBeforePointsPastTheWholeFoldedGroup() {
        var c = new JobGroupCollapser(1);
        c.Feed([Row(4), Row(3), Row(2), Row(1, "updated")]);
        var page = c.Finish();

        Assert.Single(page.Rows);
        Assert.Equal(3, page.Rows[0].Repeat);
        Assert.Equal(2L, page.NextBefore);
    }

    [Fact]
    public void FeedStopsOnceThePageIsFull() {
        var c = new JobGroupCollapser(1);
        c.Feed([Row(3, "updated"), Row(2, "failed"), Row(1)]);

        Assert.Single(c.Finish().Rows);
        Assert.Equal(3L, c.Finish().NextBefore);
    }

    [Fact]
    public void EmptySourceYieldsAnEmptyPage() {
        var page = new JobGroupCollapser(20).Finish();

        Assert.Empty(page.Rows);
        Assert.Null(page.NextBefore);
    }

    [Fact]
    public void TakeIsClampedToOneHundred() {
        Assert.Equal(1, JobGroupCollapser.ClampTake(0));
        Assert.Equal(1, JobGroupCollapser.ClampTake(-5));
        Assert.Equal(100, JobGroupCollapser.ClampTake(500));
        Assert.Equal(20, JobGroupCollapser.ClampTake(20));
    }

    [Fact]
    public void BatchIsAlwaysBiggerThanThePage() {
        Assert.Equal(40, JobGroupCollapser.BatchFor(1));
        Assert.Equal(80, JobGroupCollapser.BatchFor(20));
        Assert.Equal(200, JobGroupCollapser.BatchFor(100));
    }
}

using EggIncognito.Data.Services;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests.Predictions;

public class EventPredictorTests {
    private const double Day = 86400d;
    private const double Week = 7 * 86400d;

    private static EventOccurrence Occ(double start, double duration = 3600) => new(start, start + duration);

    private static EventOccurrence[] WeeklyStarts(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Occ(i * Week))];

    private static EventOccurrence[] FromStartsInDays(params double[] days) =>
        [.. days.Select(d => Occ(d * Day))];

    [Fact]
    public void StatsFor_FiveCollapsedStarts_ReturnsNull() =>
        Assert.Null(EventPredictor.StatsFor("piggy-boost", false, WeeklyStarts(5)));

    [Fact]
    public void StatsFor_SixCollapsedStarts_ReturnsStats() {
        var stats = EventPredictor.StatsFor("piggy-boost", true, WeeklyStarts(6));

        Assert.NotNull(stats);
        Assert.Equal(5 * Week, stats.LastStart);
        Assert.Equal(Week, stats.MedianIntervalSeconds);
        Assert.Equal(3600, stats.DurationSeconds);
        Assert.Equal(0, stats.WindowSeconds);
        Assert.Equal(6, stats.Samples);
    }

    [Fact]
    public void StatsFor_GarbageCadence_ReturnsNull() {
        var occurrences = FromStartsInDays(0, 20, 70, 98, 406, 800);
        Assert.Null(EventPredictor.StatsFor("piggy-boost", false, occurrences));
    }

    [Fact]
    public void StatsFor_SpreadJustUnderRatio_ReturnsStats() {
        var occurrences = FromStartsInDays(0, 66, 132, 232, 366, 500);

        var stats = EventPredictor.StatsFor("piggy-boost", false, occurrences);

        Assert.NotNull(stats);
        Assert.Equal(100 * Day, stats.MedianIntervalSeconds);
        Assert.Equal(34 * Day, stats.WindowSeconds);
    }

    [Fact]
    public void StatsFor_SpreadJustOverRatio_ReturnsNull() {
        var occurrences = FromStartsInDays(0, 64, 128, 228, 364, 500);
        Assert.Null(EventPredictor.StatsFor("piggy-boost", false, occurrences));
    }

    [Fact]
    public void StatsFor_SixStartsPlusNearDuplicate_CollapsesToSixAndKeepsEarlier() {
        EventOccurrence[] occurrences = [.. WeeklyStarts(6), Occ(5 * Week + 7200)];

        var stats = EventPredictor.StatsFor("piggy-boost", false, occurrences);

        Assert.NotNull(stats);
        Assert.Equal(6, stats.Samples);
        Assert.Equal(5 * Week, stats.LastStart);
        Assert.Equal(Week, stats.MedianIntervalSeconds);
    }

    [Fact]
    public void StatsFor_FiveStartsPlusNearDuplicate_ReturnsNull() {
        EventOccurrence[] occurrences = [.. WeeklyStarts(5), Occ(4 * Week + 7200)];
        Assert.Null(EventPredictor.StatsFor("piggy-boost", false, occurrences));
    }

    [Fact]
    public void StatsFor_DuplicateStartWithDifferentEnd_DoesNotSkewDurationMedian() {
        EventOccurrence[] occurrences = [.. WeeklyStarts(6), Occ(5 * Week, 999999)];

        var stats = EventPredictor.StatsFor("piggy-boost", false, occurrences);

        Assert.NotNull(stats);
        Assert.Equal(6, stats.Samples);
        Assert.Equal(3600, stats.DurationSeconds);
    }

    [Fact]
    public void IsUnstable_SpreadJustOverRatio_ReturnsTrue() => Assert.True(EventPredictor.IsUnstable(100 * Day, 36 * Day));

    [Fact]
    public void IsUnstable_SpreadJustUnderRatio_ReturnsFalse() => Assert.False(EventPredictor.IsUnstable(100 * Day, 34 * Day));

    [Fact]
    public void IsUnstable_NonPositiveMedian_ReturnsTrue() {
        Assert.True(EventPredictor.IsUnstable(0, 0));
        Assert.True(EventPredictor.IsUnstable(-5, 1));
    }

    [Fact]
    public void Project_NowBeyondStalenessBound_ReturnsNull() {
        var stats = new EventStreamStats("piggy-boost", false, 5 * Week, Week, 3600, 0, 6);
        Assert.Null(EventPredictor.Project(stats, 5 * Week + 3 * Week));
    }

    [Fact]
    public void Project_NowInsideStalenessBound_SkipsOnePeriodAndWidensWindow() {
        var stats = new EventStreamStats("piggy-boost", false, 5 * Week, Week, 3600, 50000, 6);
        double now = 5 * Week + 1.5 * Week;

        var prediction = EventPredictor.Project(stats, now);

        Assert.NotNull(prediction);
        Assert.Equal(5 * Week + 2 * Week, prediction.PredictedStart);
        Assert.Equal(1, prediction.SkippedPeriods);
        Assert.Equal(1.4826 * 50000 * Math.Sqrt(2), prediction.WindowSeconds);
        Assert.True(prediction.PredictedStart >= now);
    }

    [Fact]
    public void Project_ZeroMadStream_WindowFlooredAtRatioOfMedian() {
        var stats = new EventStreamStats("piggy-boost", false, 5 * Week, Week, 3600, 0, 6);

        var prediction = EventPredictor.Project(stats, 5 * Week + 0.5 * Week);

        Assert.NotNull(prediction);
        Assert.Equal(6 * Week, prediction.PredictedStart);
        Assert.Equal(0, prediction.SkippedPeriods);
        Assert.Equal(0.05 * Week, prediction.WindowSeconds);
    }

    [Fact]
    public void PredictGroup_RegularWeeklyCadence_RollsForwardWithFlooredWidenedWindow() {
        var occurrences = WeeklyStarts(6);
        double lastStart = 5 * Week;
        double now = lastStart + 1.5 * Week;

        var prediction = EventPredictor.PredictGroup("piggy-boost", true, occurrences, now);

        Assert.NotNull(prediction);
        Assert.Equal("piggy-boost", prediction.Type);
        Assert.True(prediction.Ultra);
        Assert.Equal(lastStart, prediction.LastStart);
        Assert.Equal(Week, prediction.MedianIntervalSeconds);
        Assert.Equal(lastStart + 2 * Week, prediction.PredictedStart);
        Assert.Equal(prediction.PredictedStart + 3600, prediction.PredictedEnd);
        Assert.Equal(0.05 * Week * Math.Sqrt(2), prediction.WindowSeconds);
        Assert.Equal(6, prediction.Samples);
        Assert.Equal(1, prediction.SkippedPeriods);
    }

    [Fact]
    public void PredictGroup_DeadStream_ReturnsNull() {
        var occurrences = WeeklyStarts(6);
        Assert.Null(EventPredictor.PredictGroup("piggy-boost", false, occurrences, 5 * Week + 3 * Week));
    }

    [Fact]
    public void PredictGroup_GarbageCadence_ReturnsNull() {
        var occurrences = FromStartsInDays(0, 20, 70, 98, 406, 800);
        Assert.Null(EventPredictor.PredictGroup("piggy-boost", false, occurrences, 801 * Day));
    }

    [Fact]
    public async Task GetAsync_CacheVersionMatchesCurrent_ProjectsCachedStatsWithoutQuerying() {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        var db = new EggIncognitoDbContext(opts);
        var version = new EventDataVersion();
        double lastStart = UnixSeconds.FromTime(DateTimeOffset.UtcNow) - 3600;
        var cached = new EventStreamStats("piggy-boost", false, lastStart, Week, 3600, 0, 6);
        var cache = new EventPredictionCache { Version = version.Version, Value = [cached] };
        var predictor = new EventPredictor(db, version, cache);

        var result = await predictor.GetAsync();

        var prediction = Assert.Single(result.Predictions);
        Assert.Equal("piggy-boost", prediction.Type);
        Assert.Equal(lastStart, prediction.LastStart);
        Assert.Equal(lastStart + Week, prediction.PredictedStart);
        Assert.Equal(0, prediction.SkippedPeriods);
        Assert.Equal(0.05 * Week, prediction.WindowSeconds);
        Assert.True(result.GeneratedAt > 0);
        Assert.True(prediction.PredictedStart >= result.GeneratedAt);
    }
}

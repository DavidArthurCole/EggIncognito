using EggIncognito.Data.Services;
using EggIncognito.Services.Predictions;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests.Predictions;

public class EventPredictorTests {
    private const double Week = 7 * 86400d;

    private static EventOccurrence Occ(double start, double duration = 3600) => new(start, start + duration);

    [Fact]
    public void PredictGroup_FewerThanFourOccurrences_ReturnsNull() {
        EventOccurrence[] occurrences = [Occ(0, 10), Occ(100, 10), Occ(200, 10)];
        Assert.Null(EventPredictor.PredictGroup("piggy-boost", false, occurrences, now: 1000));
    }

    [Fact]
    public void PredictGroup_RegularWeeklyCadence_PredictsAndRollsForwardPastNow() {
        EventOccurrence[] occurrences = [Occ(0), Occ(Week), Occ(2 * Week), Occ(3 * Week), Occ(4 * Week)];
        double lastStart = 4 * Week;
        double now = lastStart + 1.5 * Week;

        var prediction = EventPredictor.PredictGroup("piggy-boost", true, occurrences, now);

        Assert.NotNull(prediction);
        Assert.Equal("piggy-boost", prediction.Type);
        Assert.True(prediction.Ultra);
        Assert.Equal(lastStart, prediction.LastStart);
        Assert.Equal(Week, prediction.MedianIntervalSeconds);
        Assert.Equal(lastStart + 2 * Week, prediction.PredictedStart);
        Assert.Equal(prediction.PredictedStart + 3600, prediction.PredictedEnd);
        Assert.Equal(0, prediction.WindowSeconds);
        Assert.Equal(5, prediction.Samples);
        Assert.True(prediction.PredictedStart >= now);
    }

    [Fact]
    public void IsUnstable_SpreadGreaterThanMedian_ReturnsTrue() => Assert.True(EventPredictor.IsUnstable(10, 20));

    [Fact]
    public void IsUnstable_SpreadWithinMedian_ReturnsFalse() => Assert.False(EventPredictor.IsUnstable(10, 5));

    [Fact]
    public void IsUnstable_NonPositiveMedian_ReturnsTrue() {
        Assert.True(EventPredictor.IsUnstable(0, 0));
        Assert.True(EventPredictor.IsUnstable(-5, 1));
    }

    [Fact]
    public void StatsFor_RegularWeeklyCadence_HoldsNoTimeBakedValues() {
        EventOccurrence[] occurrences = [Occ(0), Occ(Week), Occ(2 * Week), Occ(3 * Week), Occ(4 * Week)];

        var stats = EventPredictor.StatsFor("piggy-boost", true, occurrences);

        Assert.NotNull(stats);
        Assert.Equal(4 * Week, stats.LastStart);
        Assert.Equal(Week, stats.MedianIntervalSeconds);
        Assert.Equal(3600, stats.DurationSeconds);
        Assert.Equal(0, stats.WindowSeconds);
        Assert.Equal(5, stats.Samples);
    }

    [Fact]
    public void Project_SameStatsAtLaterNow_RollsForwardAgain() {
        var stats = new EventStreamStats("piggy-boost", false, 4 * Week, Week, 3600, 0, 5);

        var early = EventPredictor.Project(stats, 4 * Week + 0.5 * Week);
        var late = EventPredictor.Project(stats, 4 * Week + 3.5 * Week);

        Assert.Equal(5 * Week, early.PredictedStart);
        Assert.Equal(8 * Week, late.PredictedStart);
    }

    [Fact]
    public async Task GetAsync_CacheVersionMatchesCurrent_ProjectsCachedStatsWithoutQuerying() {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        var db = new EggIncognitoDbContext(opts);
        var version = new EventDataVersion();
        var cached = new EventStreamStats("piggy-boost", false, 0, Week, 3600, 0, 5);
        var cache = new EventPredictionCache { Version = version.Version, Value = [cached] };
        var predictor = new EventPredictor(db, version, cache);

        var result = await predictor.GetAsync();

        var prediction = Assert.Single(result.Predictions);
        Assert.Equal("piggy-boost", prediction.Type);
        Assert.Equal(cached.LastStart, prediction.LastStart);
        Assert.True(result.GeneratedAt > 0);
        Assert.True(prediction.PredictedStart >= result.GeneratedAt);
    }
}

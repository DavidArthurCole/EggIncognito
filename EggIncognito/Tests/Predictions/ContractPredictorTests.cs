using EggIncognito.Data.Services;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EggIncognito.Tests.Predictions;

public class ContractPredictorTests {
    private static readonly DateTimeOffset Base = new(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static ContractReleaseSample Sample(
        string contractId, int dayOffset, int prophecyEggs = 0, bool ultra = false, string? name = null) =>
        new(contractId, name ?? contractId, UnixSeconds.FromTime(Base.AddDays(dayOffset)), prophecyEggs, ultra);

    private static ContractReleaseSample At(
        string contractId, DateTimeOffset start, int prophecyEggs = 0, bool ultra = false) =>
        new(contractId, contractId, UnixSeconds.FromTime(start), prophecyEggs, ultra);

    private static double Utc(int year, int month, int day, int hour) =>
        UnixSeconds.FromTime(new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero));

    private static DateTimeOffset Local(double unixSeconds) =>
        TimeZoneInfo.ConvertTime(UnixSeconds.ToTime(unixSeconds), Zone);

    private static EggIncognitoDbContext UnreachableDb() =>
        new(new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1").Options);

    private static (ContractPredictor Predictor, ContractDataVersion Version, ContractPredictionCache Cache) Primed(
        IReadOnlyList<ContractReleaseSample> samples) {
        var version = new ContractDataVersion();
        var cache = new ContractPredictionCache {
            Value = ContractPredictor.BuildData(samples),
            Version = version.Version
        };
        return (new ContractPredictor(UnreachableDb(), version, cache), version, cache);
    }

    [Fact]
    public void BuildData_AssignsPoolsByProphecyEggsAndUltra() {
        ContractReleaseSample[] samples = [
            Sample("plain", 0),
            Sample("pe", 0, prophecyEggs: 1),
            Sample("ultra", 0, prophecyEggs: 3, ultra: true)
        ];

        var data = ContractPredictor.BuildData(samples);

        Assert.Equal("plain", Assert.Single(data.Pools[ContractSlotKind.Leggacy]).ContractId);
        Assert.Equal("pe", Assert.Single(data.Pools[ContractSlotKind.PeLeggacy]).ContractId);
        Assert.Equal("ultra", Assert.Single(data.Pools[ContractSlotKind.PeLeggacyUltra]).ContractId);
        Assert.Empty(data.Pools[ContractSlotKind.NewContract]);
        Assert.Equal(0, data.PoolGapSeconds[ContractSlotKind.NewContract]);
    }

    [Fact]
    public void BuildData_AnyUltraReleaseMakesTheContractUltra() {
        ContractReleaseSample[] samples = [
            Sample("a", -14, prophecyEggs: 1),
            Sample("a", 0, prophecyEggs: 1, ultra: true)
        ];

        var data = ContractPredictor.BuildData(samples);

        Assert.Empty(data.Pools[ContractSlotKind.PeLeggacy]);
        Assert.Equal("a", Assert.Single(data.Pools[ContractSlotKind.PeLeggacyUltra]).ContractId);
    }

    [Fact]
    public void BuildData_CandidatesOldestFirstNamedByNewestRelease() {
        ContractReleaseSample[] samples = [
            Sample("a", -30), Sample("a", -2, name: "A Latest"),
            Sample("b", -10),
            Sample("c", -20)
        ];
        string[] expected = ["c", "b", "a"];

        var pool = ContractPredictor.BuildData(samples).Pools[ContractSlotKind.Leggacy];

        Assert.Equal(expected, pool.Select(c => c.ContractId).ToList());
        var newest = pool.Single(c => c.ContractId == "a");
        Assert.Equal("A Latest", newest.Name);
        Assert.Equal(UnixSeconds.FromTime(Base.AddDays(-2)), newest.LastReleased);
        Assert.Equal(2, newest.Releases);
    }

    [Fact]
    public void Top_TakesFiveOldestCandidates() {
        var samples = Enumerable.Range(0, 8).Select(i => Sample($"c{i}", -i)).ToList();

        var top = ContractPredictor.Top(ContractPredictor.BuildData(samples), ContractSlotKind.Leggacy);

        Assert.Equal(5, top.Count);
        Assert.Equal("c7", top[0].ContractId);
        Assert.Equal("c3", top[4].ContractId);
    }

    [Fact]
    public void BuildData_PoolGapIsMedianOfSuccessiveGaps() {
        ContractReleaseSample[] samples = [
            Sample("a", -21), Sample("a", -14), Sample("a", 0),
            Sample("b", -30), Sample("b", -16)
        ];

        var data = ContractPredictor.BuildData(samples);

        Assert.Equal(14 * 86400d, data.PoolGapSeconds[ContractSlotKind.Leggacy]);
    }

    [Fact]
    public void BuildData_PoolGapFallsBackToOneWeekPerContract() {
        ContractReleaseSample[] samples = [
            Sample("a", 0, prophecyEggs: 1),
            Sample("b", -3, prophecyEggs: 1)
        ];

        var data = ContractPredictor.BuildData(samples);

        Assert.Equal(2 * 7 * 86400d, data.PoolGapSeconds[ContractSlotKind.PeLeggacy]);
    }

    [Fact]
    public void SnapToSlot_EstimateOnASlot_KeepsIt() {
        double slot = Utc(2026, 6, 17, 16);
        Assert.Equal(slot, ContractPredictor.SnapToSlot(slot, ContractSlotKind.Leggacy));
    }

    [Fact]
    public void SnapToSlot_EstimateBetweenSlots_MovesForward() {
        Assert.Equal(
            Utc(2026, 6, 19, 16),
            ContractPredictor.SnapToSlot(Utc(2026, 6, 17, 20), ContractSlotKind.PeLeggacyUltra));
    }

    [Fact]
    public async Task GetContractAsync_UnknownId_ReturnsNull() {
        var (predictor, _, _) = Primed([Sample("known", 0)]);
        Assert.Null(await predictor.GetContractAsync("missing"));
    }

    [Fact]
    public async Task GetContractAsync_KnownId_EstimateSnapsToPoolWeekday() {
        var now = DateTimeOffset.UtcNow;
        ContractReleaseSample[] samples = [
            At("a", now.AddDays(-14), 1),
            At("a", now, 1)
        ];
        var (predictor, _, _) = Primed(samples);

        var estimate = await predictor.GetContractAsync("a");

        Assert.NotNull(estimate);
        Assert.Equal(ContractSlotKind.PeLeggacy, estimate.Pool);
        Assert.Equal(2, estimate.Samples);
        Assert.Equal(UnixSeconds.FromTime(now), estimate.LastReleased);
        Assert.True(estimate.EstimatedNext >= estimate.LastReleased + 14 * 86400d);
        Assert.Equal(DayOfWeek.Friday, Local(estimate.EstimatedNext).DayOfWeek);
        Assert.Equal(12, Local(estimate.EstimatedNext).Hour);
    }

    [Fact]
    public async Task GetContractAsync_LastReleasedLongAgo_EstimateIsNotInThePast() {
        var now = DateTimeOffset.UtcNow;
        ContractReleaseSample[] samples = [
            At("a", now.AddDays(-800), 1),
            At("a", now.AddDays(-786), 1)
        ];
        var (predictor, _, _) = Primed(samples);

        var estimate = await predictor.GetContractAsync("a");

        Assert.NotNull(estimate);
        Assert.Equal(UnixSeconds.FromTime(now.AddDays(-786)), estimate.LastReleased);
        Assert.True(estimate.EstimatedNext >= UnixSeconds.FromTime(now));
        Assert.Equal(DayOfWeek.Friday, Local(estimate.EstimatedNext).DayOfWeek);
        Assert.Equal(12, Local(estimate.EstimatedNext).Hour);
    }

    [Fact]
    public async Task GetSlotsAsync_CacheCurrent_ReusesCachedDataWithoutDatabase() {
        var (predictor, _, _) = Primed([Sample("a", 0)]);

        var response = await predictor.GetSlotsAsync(3);

        Assert.InRange(response.Slots.Count, 3, 4);
        Assert.All(response.Slots, s => Assert.True(s.Candidates.Count <= 5));
        Assert.All(
            response.Slots.Where(s => s.Kind == ContractSlotKind.NewContract),
            s => Assert.Empty(s.Candidates));
    }

    [Fact]
    public async Task GetSlotsAsync_AfterVersionBump_RecomputesAndReachesDatabase() {
        var (predictor, version, _) = Primed([Sample("a", 0)]);
        version.Bump();

        var thrown = await Record.ExceptionAsync(() => predictor.GetSlotsAsync(3));

        Assert.NotNull(thrown);
        Assert.True(thrown is NpgsqlException or InvalidOperationException, thrown.ToString());
    }

    [Fact]
    public void ContractPredictionCache_DefaultsForceInitialRecompute() {
        var cache = new ContractPredictionCache();
        Assert.Equal(-1, cache.Version);
        Assert.Null(cache.Value);
    }
}

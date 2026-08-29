using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

public class ContractSlotsTests {
    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static double UtcSeconds(int year, int month, int day, int hour) =>
        UnixSeconds.FromTime(Utc(year, month, day, hour));

    [Fact]
    public void Next_FromTuesdayInSummer_UsesEdtNoon() {
        var slots = ContractSlots.Next(Utc(2026, 6, 16, 8), 1);

        var slot = Assert.Single(slots);
        Assert.Equal(UtcSeconds(2026, 6, 17, 16), slot.Time);
        Assert.Equal(ContractSlotKind.Leggacy, slot.Kind);
    }

    [Fact]
    public void Next_FromTuesdayInWinter_UsesEstNoon() {
        var slots = ContractSlots.Next(Utc(2026, 1, 13, 8), 1);

        var slot = Assert.Single(slots);
        Assert.Equal(UtcSeconds(2026, 1, 14, 17), slot.Time);
        Assert.Equal(ContractSlotKind.Leggacy, slot.Kind);
    }

    [Fact]
    public void Next_AcrossWeekBoundary_OrderedAndFridayPairShareTime() {
        var slots = ContractSlots.Next(Utc(2026, 6, 19, 20), 5);

        ContractSlotKind[] expected = [
            ContractSlotKind.NewContract, ContractSlotKind.Leggacy,
            ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra, ContractSlotKind.NewContract
        ];

        Assert.Equal(5, slots.Count);
        Assert.Equal(expected, slots.Select(s => s.Kind).ToList());
        Assert.Equal(UtcSeconds(2026, 6, 22, 16), slots[0].Time);
        Assert.Equal(UtcSeconds(2026, 6, 24, 16), slots[1].Time);
        Assert.Equal(UtcSeconds(2026, 6, 26, 16), slots[2].Time);
        Assert.Equal(slots[2].Time, slots[3].Time);
        Assert.Equal(UtcSeconds(2026, 6, 29, 16), slots[4].Time);
        Assert.Equal([.. slots.Select(s => s.Time).Order()], [.. slots.Select(s => s.Time)]);
    }

    [Fact]
    public void Next_AtExactSlotTime_SkipsThatSlot() {
        var slots = ContractSlots.Next(Utc(2026, 6, 19, 16), 1);

        var slot = Assert.Single(slots);
        Assert.Equal(ContractSlotKind.NewContract, slot.Kind);
        Assert.Equal(UtcSeconds(2026, 6, 22, 16), slot.Time);
    }

    [Fact]
    public void Next_HorizonSplittingFridayPair_KeepsBoth() {
        var slots = ContractSlots.Next(Utc(2026, 6, 17, 20), 1);

        Assert.Equal(2, slots.Count);
        Assert.Equal(ContractSlotKind.PeLeggacy, slots[0].Kind);
        Assert.Equal(ContractSlotKind.PeLeggacyUltra, slots[1].Kind);
        Assert.Equal(slots[0].Time, slots[1].Time);
    }

    [Fact]
    public void Next_HorizonRespectedWhenNoPairSplit() {
        var slots = ContractSlots.Next(Utc(2026, 6, 17, 20), 3);

        Assert.Equal(3, slots.Count);
        Assert.Equal(UtcSeconds(2026, 6, 22, 16), slots[2].Time);
    }

    [Fact]
    public void Next_NonPositiveHorizon_ReturnsEmpty() =>
        Assert.Empty(ContractSlots.Next(Utc(2026, 6, 17, 20), 0));
}

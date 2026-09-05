using EggIncognito.Models.Events;
using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

public class EventBacktestTests {
    [Fact]
    public void Run_TemplateHistory_HitsEverySlotAndEveryFixedType() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-364), EventTemplate.End.AddDays(120));

        var result = EventBacktest.Run(rows, EventTemplate.AsOf, 28);

        Assert.Equal(EventTemplate.AsOf, result.AsOf);
        Assert.Equal(28, result.HorizonDays);
        Assert.Equal(0, result.ActualUncovered);
        Assert.All(result.Kinds, k => {
            Assert.True(k.Predicted > 0);
            Assert.Equal(k.Predicted, k.SlotHit);
            Assert.True(k.Top3Hit >= k.TypeHit);
        });
        var fixedKind = result.Kinds.Single(k => k.Kind == EventPredictionKind.Fixed);
        Assert.Equal(fixedKind.Predicted, fixedKind.TypeHit);
    }

    [Fact]
    public void Run_NoHistoryInWindow_PredictsNothingAndCountsActualAsUncovered() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-500), EventTemplate.End.AddDays(-200));
        rows.AddRange(EventTemplate.Build(EventTemplate.End.AddDays(1), EventTemplate.End.AddDays(20)));

        var result = EventBacktest.Run(rows, EventTemplate.AsOf, 28);

        Assert.All(result.Kinds, k => Assert.Equal(0, k.Predicted));
        Assert.True(result.ActualUncovered > 0);
    }

    [Fact]
    public void Run_HorizonOutOfRange_ClampsToNinetyDays() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-364), EventTemplate.End.AddDays(200));

        var result = EventBacktest.Run(rows, EventTemplate.AsOf, 500);

        Assert.Equal(90, result.HorizonDays);
    }
}

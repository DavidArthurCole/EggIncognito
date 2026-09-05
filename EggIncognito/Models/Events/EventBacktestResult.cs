namespace EggIncognito.Models.Events;

public sealed record EventBacktestResult(
    double AsOf,
    int HorizonDays,
    IReadOnlyList<EventBacktestKindResult> Kinds,
    int ActualUncovered);

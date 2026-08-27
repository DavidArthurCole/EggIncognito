namespace EggIncognito.Models.Events;

public sealed record EventCalendarRow(
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<EventCalendarCell> Cells,
    IReadOnlyList<IReadOnlyList<EventCalendarBar>> Lanes,
    double? NowPercent);

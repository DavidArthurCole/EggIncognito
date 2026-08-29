namespace EggIncognito.Models.Events;

public sealed record EventCalendarBar(
    CalendarItem Item,
    int Lane,
    double LeftPercent,
    double WidthPercent,
    bool Active,
    bool Past,
    bool ContinuesLeft,
    bool ContinuesRight);

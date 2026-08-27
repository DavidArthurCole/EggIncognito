namespace EggIncognito.Models.Events;

public sealed record EventCalendarBar(
    GameEventDto Event,
    int Lane,
    double LeftPercent,
    double WidthPercent,
    bool Active,
    bool Past,
    bool ContinuesLeft,
    bool ContinuesRight);

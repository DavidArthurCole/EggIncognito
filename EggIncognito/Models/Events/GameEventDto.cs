namespace EggIncognito.Models.Events;

public sealed record GameEventDto(
    string Id,
    string Type,
    string Message,
    double Multiplier,
    bool Ultra,
    double StartTimestamp,
    double EndTimestamp,
    string Source);

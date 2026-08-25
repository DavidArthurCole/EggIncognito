namespace EggIncognito.Models.Events;

public sealed record GameEventObservation(
    string EventId,
    string EventType,
    string Message,
    double Multiplier,
    bool Ultra,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Source,
    DateTimeOffset? SeenAt);

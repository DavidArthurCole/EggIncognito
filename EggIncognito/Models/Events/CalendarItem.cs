using EggIncognito.Models.Contracts;

namespace EggIncognito.Models.Events;

public sealed record CalendarItem(
    string Key,
    double Start,
    double End,
    CalendarItemKind Kind,
    GameEventDto? Event,
    ContractReleaseDto? Contract,
    EventPrediction? Prediction,
    ContractSlotPrediction? Slot);

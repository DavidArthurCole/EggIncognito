namespace EggIncognito.Models.Events;

public sealed record EventBacktestKindResult(
    EventPredictionKind Kind,
    int Predicted,
    int SlotHit,
    int TypeHit,
    int Top3Hit);

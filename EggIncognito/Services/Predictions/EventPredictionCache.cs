namespace EggIncognito.Services.Predictions;

public sealed class EventPredictionCache {
    public long Version { get; set; } = -1;
    public IReadOnlyList<EventStreamStats>? Value { get; set; }
}

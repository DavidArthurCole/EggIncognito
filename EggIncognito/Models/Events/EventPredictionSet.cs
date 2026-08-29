namespace EggIncognito.Models.Events;

public sealed record EventPredictionSet(double GeneratedAt, IReadOnlyList<EventPrediction> Predictions);

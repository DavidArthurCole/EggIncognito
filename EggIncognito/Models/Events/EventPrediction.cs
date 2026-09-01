namespace EggIncognito.Models.Events;

public sealed record EventPrediction(
    string Type,
    bool Ultra,
    double LastStart,
    double MedianIntervalSeconds,
    double PredictedStart,
    double PredictedEnd,
    double WindowSeconds,
    int Samples,
    int SkippedPeriods);

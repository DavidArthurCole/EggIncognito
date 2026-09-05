namespace EggIncognito.Models.Events;

public sealed record EventPrediction(
    string? Type,
    bool Ultra,
    EventPredictionKind Kind,
    double PredictedStart,
    double PredictedEnd,
    double Confidence,
    IReadOnlyList<EventCandidate> Candidates,
    int Observed,
    int Expected,
    int PeriodDays,
    double LastStart);

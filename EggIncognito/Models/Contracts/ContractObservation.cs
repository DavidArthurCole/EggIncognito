namespace EggIncognito.Models.Contracts;

public sealed record ContractObservation(
    string ContractId,
    string Name,
    int Egg,
    string? CustomEggId,
    string? SeasonId,
    DateTimeOffset Start,
    DateTimeOffset End,
    double LengthSeconds,
    bool Leggacy,
    bool UltraOnly,
    int ProphecyEggs,
    bool CoopAllowed,
    int MaxCoopSize,
    double MinutesPerToken,
    byte[] Proto,
    string Source,
    DateTimeOffset SeenAt);

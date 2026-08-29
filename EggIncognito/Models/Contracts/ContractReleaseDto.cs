namespace EggIncognito.Models.Contracts;

public sealed record ContractReleaseDto(
    long Id,
    string ContractId,
    string Name,
    int Egg,
    string? CustomEggId,
    string? SeasonId,
    double StartTimestamp,
    double EndTimestamp,
    double LengthSeconds,
    bool Leggacy,
    bool UltraOnly,
    int ProphecyEggs,
    bool CoopAllowed,
    int MaxCoopSize,
    string Source);

namespace EggIncognito.Models.Devices;

public sealed record RecertResultDto(
    bool Ok,
    IReadOnlyList<string> Log,
    IReadOnlyDictionary<string, string> Fields,
    string? FailedStep,
    int ShotCount,
    IReadOnlyList<RecertShotDto>? Shots = null);

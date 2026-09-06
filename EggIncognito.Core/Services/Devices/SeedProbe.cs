namespace EggIncognito.Core.Services.Devices;

public sealed record SeedProbe(bool Ran, bool SeededImage, string? State, string? Service, string? LastLog);

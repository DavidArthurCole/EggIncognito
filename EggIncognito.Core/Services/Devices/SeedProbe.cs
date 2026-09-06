namespace EggIncognito.Core.Services.Devices;

public sealed record SeedProbe(bool SeededImage, string? State, string? Service, string? LastLog);

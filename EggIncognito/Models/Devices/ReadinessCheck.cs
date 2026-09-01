namespace EggIncognito.Models.Devices;

public sealed record ReadinessCheck(bool Ok, string? Note = null);

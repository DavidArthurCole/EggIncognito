namespace EggIncognito.Models.Devices;

public sealed record DeviceCookbookStart(
    DeviceCookbookStartOutcome Outcome,
    long JobId = 0,
    string? Error = null);

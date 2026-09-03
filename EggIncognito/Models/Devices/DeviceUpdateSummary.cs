namespace EggIncognito.Models.Devices;

public sealed record DeviceUpdateSummary(string? Status, string? Note, string? By, DateTimeOffset At);

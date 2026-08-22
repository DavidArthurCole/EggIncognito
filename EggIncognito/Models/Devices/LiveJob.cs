namespace EggIncognito.Models.Devices;

public sealed record LiveJob(string Device, long Id, string Kind, string? Message, DateTimeOffset StartedAt);

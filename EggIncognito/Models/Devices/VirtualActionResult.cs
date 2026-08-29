namespace EggIncognito.Models.Devices;

public sealed record VirtualActionResult(bool Ok, string Outcome, string? InstanceId, string? Note);

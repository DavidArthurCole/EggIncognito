namespace EggIncognito.Models.Devices;

public sealed record TransportClaimResult(bool Ok, DateTimeOffset ExpiresAt);

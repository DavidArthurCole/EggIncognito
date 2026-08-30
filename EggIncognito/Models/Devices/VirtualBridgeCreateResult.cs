namespace EggIncognito.Models.Devices;

public sealed record VirtualBridgeCreateResult(
    bool Ok,
    string Outcome,
    string? Note,
    VirtualBridgeInstance? Instance);

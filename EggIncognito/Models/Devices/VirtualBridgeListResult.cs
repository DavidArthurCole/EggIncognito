namespace EggIncognito.Models.Devices;

public sealed record VirtualBridgeListResult(
    bool Ok,
    string Outcome,
    string? Note,
    IReadOnlyList<VirtualBridgeInstance> Instances);

namespace EggIncognito.Models.Devices;

public sealed record VirtualDevicesStatus(
    bool Enabled,
    bool Supported,
    string Kind,
    string Image,
    int MaxInstances,
    int LiveCount,
    string? Note,
    IReadOnlyDictionary<string, int> ByState,
    IReadOnlyList<VirtualInstanceRow> Instances);

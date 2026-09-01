namespace EggIncognito.Models.Devices;

public sealed record ModuleCacheRow(
    string Name, string? Version, long ByteSize, string Source, bool Cached, string? Error);

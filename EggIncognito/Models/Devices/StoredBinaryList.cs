namespace EggIncognito.Models.Devices;

public sealed record StoredBinaryList(bool Ok, int Count, List<StoredBinaryRow> Binaries);

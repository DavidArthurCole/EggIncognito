namespace EggIncognito.Models.Devices;

public sealed record StoredBinaryRow(
    string Platform,
    string Version,
    string Sha256,
    long ByteSize,
    int NativeSymbols,
    int EffectiveSymbols,
    string Source,
    DateTimeOffset PulledAt);

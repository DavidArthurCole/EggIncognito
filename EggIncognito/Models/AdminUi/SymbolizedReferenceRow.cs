namespace EggIncognito.Models.AdminUi;

public record SymbolizedReferenceRow(
    string Platform,
    string Version,
    string Sha256,
    long ByteSize,
    int SymbolCount,
    DateTimeOffset UploadedAt);

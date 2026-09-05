namespace EggIncognito.Models.Devices;

public sealed record IntegrityAssetsView(
    bool Ok,
    string? Error,
    string? Model,
    string? Product,
    string? Fingerprint,
    string? ReleasedOn,
    string? Expiry,
    string? PatchDate,
    string? KeyboxSource,
    int KeyboxCerts,
    string? KeyboxNote,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Warnings);

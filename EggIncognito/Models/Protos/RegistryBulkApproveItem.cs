namespace EggIncognito.Models.Protos;

public sealed record RegistryBulkApproveItem(
    int Id,
    string? Platform,
    string? AppVersion,
    string? Build,
    string? ClientVersion);

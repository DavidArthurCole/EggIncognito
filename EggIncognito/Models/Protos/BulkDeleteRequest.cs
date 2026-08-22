namespace EggIncognito.Models.Protos;

public sealed record BulkDeleteRequest(IReadOnlyList<RegistryVersionKey> Versions);

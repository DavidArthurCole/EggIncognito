namespace EggIncognito.Models.Protos;

public sealed record BulkApproveRequest(IReadOnlyList<RegistryBulkApproveItem> Items);

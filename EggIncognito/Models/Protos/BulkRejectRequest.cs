namespace EggIncognito.Models.Protos;

public sealed record BulkRejectRequest(IReadOnlyList<int> Ids, string? Note);

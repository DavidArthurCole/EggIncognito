namespace EggIncognito.Models.Staging;

public sealed record BulkApproveResp(int Approved, int Skipped, int Failed);

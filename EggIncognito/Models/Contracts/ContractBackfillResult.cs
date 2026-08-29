namespace EggIncognito.Models.Contracts;

public sealed record ContractBackfillResult(int Scanned, int Inserted, int Updated, int Skipped);

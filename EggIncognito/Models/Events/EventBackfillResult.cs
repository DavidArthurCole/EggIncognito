namespace EggIncognito.Models.Events;

public sealed record EventBackfillResult(int Scanned, int Inserted, int Updated, int Skipped);

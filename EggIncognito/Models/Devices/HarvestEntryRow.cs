namespace EggIncognito.Models.Devices;

public sealed record HarvestEntryRow(
    DateTimeOffset RanAt,
    string? Entry,
    string? Kind,
    string Outcome,
    string? Note,
    long Bytes,
    string? Sha256);

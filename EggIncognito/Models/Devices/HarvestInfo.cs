namespace EggIncognito.Models.Devices;

public sealed record HarvestInfo(
    string Device,
    string Platform,
    string? AppVersion,
    string? Build,
    bool InRegistry,
    string Revision,
    string? HarvestedRevision,
    bool Stale,
    bool Dirty,
    bool Harvesting,
    DateTimeOffset? LastHarvestAt,
    string LastHarvestStatus,
    string? LastHarvestNote,
    List<HarvestEntryRow> Entries);

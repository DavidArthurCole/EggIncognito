namespace EggIncognito.Models.Devices;

public sealed record JobPage(IReadOnlyList<JobGroupRow> Rows, long? NextBefore);

namespace EggIncognito.Models.Contributions;

public sealed record ContributionSummaryDto(
    bool Enabled,
    int Recorded,
    int Submitted,
    int Approved,
    int Rejected,
    int MaxRecordedPerUser,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> Routes);

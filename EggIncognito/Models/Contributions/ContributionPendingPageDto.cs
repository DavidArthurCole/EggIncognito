namespace EggIncognito.Models.Contributions;

public sealed record ContributionPendingPageDto(
    int Total,
    IReadOnlyList<ContributionPendingRowDto> Rows);

namespace EggIncognito.Models.Contributions;

public sealed record ContributionTalliesDto(
    ContributionCountsDto Counts,
    IReadOnlyList<ContributionTallyDto> Tallies);

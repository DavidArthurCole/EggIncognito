namespace EggIncognito.Models.Contributions;

public sealed record ContributionTallyDto(
    Guid ContributorUserId,
    string Kind,
    int Submitted,
    DateTimeOffset Oldest);

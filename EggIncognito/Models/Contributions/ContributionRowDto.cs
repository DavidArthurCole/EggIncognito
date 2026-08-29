namespace EggIncognito.Models.Contributions;

public sealed record ContributionRowDto(
    long Id,
    string Kind,
    string Status,
    string Summary,
    string? ClientVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? SubmittedAt);

namespace EggIncognito.Models.Contributions;

public sealed class ContributionReviewRequest {
    public IReadOnlyList<long> Ids { get; set; } = [];
    public bool Approve { get; set; }
    public string? Note { get; set; }
}

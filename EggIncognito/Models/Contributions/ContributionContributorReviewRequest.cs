namespace EggIncognito.Models.Contributions;

public sealed class ContributionContributorReviewRequest {
    public Guid ContributorUserId { get; set; }
    public string Kind { get; set; } = "";
    public bool Approve { get; set; }
    public string? Note { get; set; }
}

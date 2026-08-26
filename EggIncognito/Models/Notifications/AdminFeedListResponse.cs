namespace EggIncognito.Models.Notifications;

public sealed record AdminFeedListResponse(
    int Total,
    int ActiveCount,
    int Owners,
    IReadOnlyList<AdminFeedRow> Rows);

using EggIncognito.Models.Shared;

namespace EggIncognito.Models.Staging;

public sealed record StagedReviewStatus(StatusNoteKind Kind, string Text, BulkApproveResp? Bulk = null);

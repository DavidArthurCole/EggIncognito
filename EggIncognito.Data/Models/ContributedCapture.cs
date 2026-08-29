using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("contributed_captures")]
public sealed class ContributedCapture {
    [Key][Column("id")] public long Id { get; set; }
    [Column("contributor_user_id")] public Guid ContributorUserId { get; set; }
    [Column("kind")] public string Kind { get; set; } = "";
    [Column("status")] public string Status { get; set; } = ContributedCaptureStatus.Recorded;
    [Column("summary")] public string Summary { get; set; } = "";
    [Column("payload")] public string Payload { get; set; } = "{}";
    [Column("dedupe_hash")] public string DedupeHash { get; set; } = "";
    [Column("client_version")] public string? ClientVersion { get; set; }
    [Column("recorded_at")] public DateTimeOffset RecordedAt { get; set; }
    [Column("submitted_at")] public DateTimeOffset? SubmittedAt { get; set; }
    [Column("reviewed_by")] public string? ReviewedBy { get; set; }
    [Column("reviewed_at")] public DateTimeOffset? ReviewedAt { get; set; }
    [Column("review_note")] public string? ReviewNote { get; set; }
}

public static class ContributedCaptureStatus {
    public const string Recorded = "recorded";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static bool IsKnown(string? status) =>
        status is Recorded or Submitted or Approved or Rejected;
}

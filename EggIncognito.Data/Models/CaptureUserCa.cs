using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("capture_user_cas")]
public class CaptureUserCa {
    [Key]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("discord_id")]
    public string? DiscordId { get; set; }

    [Column("pfx")]
    public byte[] Pfx { get; set; } = [];

    [Column("thumbprint")]
    public string Thumbprint { get; set; } = "";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

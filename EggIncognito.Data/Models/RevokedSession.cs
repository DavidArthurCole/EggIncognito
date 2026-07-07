using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// One row per OIDC session Authentik has told us (via back-channel logout) to end. Checked on every
// cookie-auth request against the session's sid claim; RevokedAt is kept only for cleanup/audit.
[Table("revoked_sessions")]
public class RevokedSession
{
    [Key]
    [Column("sid")]
    public string Sid { get; set; } = "";

    [Column("revoked_at")]
    public DateTimeOffset RevokedAt { get; set; }
}

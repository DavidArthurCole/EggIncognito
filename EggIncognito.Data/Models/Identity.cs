using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// Links one login method (provider, subject) to a user_id. A user with both Discord and
// Authentik logins has two rows here pointing at the same user_id.
[Table("identities")]
public class Identity
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("provider")]
    public string Provider { get; set; } = "";

    [Column("subject")]
    public string Subject { get; set; } = "";

    [Column("linked_at")]
    public DateTimeOffset LinkedAt { get; set; }
}

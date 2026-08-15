using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("site_theme_policy")]
public sealed class SiteThemePolicy {
    [Column("id")] public int Id { get; set; }
    [Column("custom_css_enabled")] public bool CustomCssEnabled { get; set; }
    [Column("default_theme_id")] public long? DefaultThemeId { get; set; }
    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [Column("updated_by_user_id")] public Guid? UpdatedByUserId { get; set; }
}

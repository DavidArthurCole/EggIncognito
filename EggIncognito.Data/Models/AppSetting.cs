using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("app_settings")]
public sealed class AppSetting {
    [Key][Column("key")] public string Key { get; set; } = "";

    [Column("value")] public string Value { get; set; } = "";

    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

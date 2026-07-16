using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;
[Table("devices")]
public sealed class Device
{
    [Column("id")] public string Id { get; set; } = "";
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("label")] public string Label { get; set; } = "";
    [Column("target")] public string Target { get; set; } = "";
    [Column("package")] public string Package { get; set; } = "com.auxbrain.egginc";
    [Column("enabled")] public bool Enabled { get; set; } = true;
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

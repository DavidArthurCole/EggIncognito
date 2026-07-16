using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;
[Table("tags")]
public class Tag
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("slug")]
    public string Slug { get; set; } = "";

    [Column("label")]
    public string Label { get; set; } = "";

    [Column("color")]
    public string? Color { get; set; }
}

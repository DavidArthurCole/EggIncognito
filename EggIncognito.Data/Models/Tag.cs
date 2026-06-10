using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A broad API "bucket" (Coops, Backups, Shells, Contracts, Artifacts, ...). Seeded from a known list
// on boot; admins can add more. `slug` is the stable key, `label` the display text, `color` an
// optional hex accent for the chip. Applied to subjects via SubjectTag. Public reads; admin manages
// the catalog (mirrors the admin-only posture for catalog-level data).
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

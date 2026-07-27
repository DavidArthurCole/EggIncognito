using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("game_data_documents")]
public class GameDataDocument {
    [Key][Column("id")] public string Id { get; set; } = "";

    [Column("json")] public string Json { get; set; } = "";

    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

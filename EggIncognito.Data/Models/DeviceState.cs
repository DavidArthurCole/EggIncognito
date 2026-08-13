using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("device_state")]
public sealed class DeviceState {
    [Key][Column("device_id")] public string DeviceId { get; set; } = "";

    [Column("platform")] public string Platform { get; set; } = "";

    [Column("package")] public string Package { get; set; } = "";

    [Column("app_version")] public string? AppVersion { get; set; }

    [Column("build")] public string? Build { get; set; }

    [Column("client_version")] public int? ClientVersion { get; set; }

    [Column("revision")] public string Revision { get; set; } = "";

    [Column("harvested_revision")] public string? HarvestedRevision { get; set; }

    [Column("dirty")] public bool Dirty { get; set; }

    [Column("harvesting")] public bool Harvesting { get; set; }

    [Column("last_harvest_at")] public DateTimeOffset? LastHarvestAt { get; set; }

    [Column("last_harvest_status")] public string LastHarvestStatus { get; set; } = "never";

    [Column("last_harvest_note")] public string? LastHarvestNote { get; set; }

    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

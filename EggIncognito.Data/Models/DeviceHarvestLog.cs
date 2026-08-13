using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("device_harvest_log")]
public sealed class DeviceHarvestLog {
    [Key][Column("id")] public long Id { get; set; }

    [Column("device_id")] public string DeviceId { get; set; } = "";

    [Column("ran_at")] public DateTimeOffset RanAt { get; set; }

    [Column("revision")] public string Revision { get; set; } = "";

    [Column("entry")] public string Entry { get; set; } = "";

    [Column("kind")] public string Kind { get; set; } = "";

    [Column("outcome")] public string Outcome { get; set; } = "";

    [Column("note")] public string? Note { get; set; }

    [Column("byte_size")] public long ByteSize { get; set; }

    [Column("sha256")] public string? Sha256 { get; set; }
}

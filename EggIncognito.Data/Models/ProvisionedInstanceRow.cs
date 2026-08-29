using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("provisioned_instances")]
public sealed class ProvisionedInstanceRow {
    [Column("instance_id")] public string InstanceId { get; set; } = "";
    [Column("kind")] public string Kind { get; set; } = "";
    [Column("image")] public string Image { get; set; } = "";
    [Column("state")] public string State { get; set; } = "creating";
    [Column("adb_serial")] public string? AdbSerial { get; set; }
    [Column("host_ref")] public string? HostRef { get; set; }
    [Column("device_id")] public string? DeviceId { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
    [Column("note")] public string? Note { get; set; }
}

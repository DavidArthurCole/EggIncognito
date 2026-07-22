using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("device_probes")]
public sealed class DeviceProbe {
    [Column("id")] public int Id { get; set; }
    [Column("device_id")] public string DeviceId { get; set; } = "";
    [Column("probed_at")] public DateTimeOffset ProbedAt { get; set; }
    [Column("reachable")] public bool Reachable { get; set; }
    [Column("installed_app_version")] public string? InstalledAppVersion { get; set; }
    [Column("installed_build")] public string? InstalledBuild { get; set; }
    [Column("latest_available")] public string? LatestAvailable { get; set; }
    [Column("result")] public string Result { get; set; } = "";
    [Column("triggered_by")] public string TriggeredBy { get; set; } = "poll";
    [Column("note")] public string? Note { get; set; }
}

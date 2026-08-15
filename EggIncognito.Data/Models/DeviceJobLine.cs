using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

public static class DeviceJobLevels {
    public const string Info = "info";
    public const string Warn = "warn";
    public const string Error = "error";
}

[Table("device_job_lines")]
public sealed class DeviceJobLine {
    [Column("id")] public long Id { get; set; }
    [Column("job_id")] public long JobId { get; set; }
    [Column("at")] public DateTimeOffset At { get; set; }
    [Column("level")] public string Level { get; set; } = DeviceJobLevels.Info;
    [Column("text")] public string Text { get; set; } = "";
    [Column("entry")] public string? Entry { get; set; }
    [Column("bytes")] public long? Bytes { get; set; }
    [Column("sha256")] public string? Sha256 { get; set; }
}

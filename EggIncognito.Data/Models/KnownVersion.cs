using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

// A version "seen in the wild" by a list source (fandom/uptodown/apkpure/itunes). Metadata only: no
// build/versionCode, no proto. The discovery list the admin UI shows; promoting one to a real registry
// row needs an APK extract. Dedup on (platform, app_version, source).
[Table("known_versions")]
public sealed class KnownVersion
{
    [Column("id")] public int Id { get; set; }
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("app_version")] public string AppVersion { get; set; } = "";
    [Column("release_date")] public DateTimeOffset? ReleaseDate { get; set; }
    [Column("changelog")] public string? Changelog { get; set; }
    [Column("source")] public string Source { get; set; } = "";
    [Column("first_seen")] public DateTimeOffset FirstSeen { get; set; }
}

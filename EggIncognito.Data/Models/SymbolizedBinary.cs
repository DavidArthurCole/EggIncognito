using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("symbolized_binaries")]
public class SymbolizedBinary {
    [Key][Column("id")] public long Id { get; set; }

    [Column("platform")] public string Platform { get; set; } = "ios";

    [Column("app_version")] public string AppVersion { get; set; } = "";

    [Column("sha256")] public string Sha256 { get; set; } = "";

    [Column("bytes")] public byte[] Bytes { get; set; } = [];

    [Column("byte_size")] public long ByteSize { get; set; }

    [Column("symbol_count")] public int SymbolCount { get; set; }

    [Column("uploaded_at")] public DateTimeOffset UploadedAt { get; set; }
}

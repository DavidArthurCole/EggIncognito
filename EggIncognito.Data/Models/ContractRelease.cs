using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("contract_releases")]
public sealed class ContractRelease {
    [Key][Column("id")] public long Id { get; set; }

    [Column("contract_id")] public string ContractId { get; set; } = "";

    [Column("name")] public string Name { get; set; } = "";

    [Column("egg")] public int Egg { get; set; }

    [Column("custom_egg_id")] public string? CustomEggId { get; set; }

    [Column("season_id")] public string? SeasonId { get; set; }

    [Column("start_time")] public DateTimeOffset StartTime { get; set; }

    [Column("end_time")] public DateTimeOffset EndTime { get; set; }

    [Column("length_seconds")] public double LengthSeconds { get; set; }

    [Column("leggacy")] public bool Leggacy { get; set; }

    [Column("ultra_only")] public bool UltraOnly { get; set; }

    [Column("prophecy_eggs")] public int ProphecyEggs { get; set; }

    [Column("coop_allowed")] public bool CoopAllowed { get; set; }

    [Column("max_coop_size")] public int MaxCoopSize { get; set; }

    [Column("minutes_per_token")] public double MinutesPerToken { get; set; }

    [Column("proto")] public byte[] Proto { get; set; } = [];

    [Column("source")] public string Source { get; set; } = "";

    [Column("first_seen_at")] public DateTimeOffset? FirstSeenAt { get; set; }

    [Column("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
}

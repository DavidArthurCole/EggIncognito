using EggIncognito.Services;

namespace EggIncognito.Models.AdminUi;

public record RouteBinaryStatus(
    DateTimeOffset? LastRefresh,
    string? BinaryVersion,
    int Discovered,
    int NewCount,
    int DriftCount,
    List<RouteBinaryRow> Rows,
    List<RouteDriftRow> Drift);
